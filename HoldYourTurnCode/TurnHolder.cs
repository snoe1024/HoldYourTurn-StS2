using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HoldYourTurn.HoldYourTurnCode;

internal static class TurnHolder
{
    private static readonly Lock TurnHoldLock = new();
    private static CancellationTokenSource? _turnHoldCancellation;

    public static bool CanPressTurnEnd
    {
        get
        {
            if (_player == null)
                return false;

            CombatManager combat = CombatManager.Instance;

            // 必須条件: 自分が現在のプレイヤーターンに参加していること。
            if (!combat.IsPartOfPlayerTurn(_player))
                return false;

            // 拒否条件: 強制終了済みのターンを取り消そうとしていること。
            if (ForcedTurnEndGuard.IsForced(_player) && combat.IsPlayerReadyToEndTurn(_player))
                return false;

            // 他modによるターン終了を阻害しないための許可設定
            if (RewindCompatibility.IsReplaying)
                return true;

            // そもそもターンが終了状態ならその取り消しのための押下は許可
            if (combat.IsPlayerReadyToEndTurn(_player))
                return true;
            
            // スマートモードがキャンセル機能を停止したら許可 (カード参照回数を減らすため末尾に配置)
            if (CanSkipProtection)
                return true;

            return CanTurnEnd;
        }
    }

    private static bool CanSkipProtection => HoldYourTurnConfig.SmartMode && _player != null
        && !SmartTurnEndEvaluator.ShouldProtectTurnEnd(_player);

    private static bool _canTurnEnd;
    public static bool CanTurnEnd
    {
        get => _canTurnEnd;
        private set => _canTurnEnd = value;
    }
    
    /// <summary>直前までに処理済みの<c>CombatHistory</c>のエントリ数(新規エントリだけを拾うため)。</summary>
    private static int _processedHistoryEntries;

    private static Player? _player;

    /// <summary>リソース獲得(手札追加/エネルギー増加/値下げによる使用可能化)を検知するたびに増える通し番号。</summary>
    private static int _gainSequence;

    /// <summary>ターン終了ボタンを押した時点での<see cref="_gainSequence"/>。未記録なら<c>null</c>。</summary>
    private static int? _gainSequenceAtPress;

    /// <summary>獲得による解除アクションを積んだが、まだ解決されていない間は真(同じ獲得列で二重に積まない)。</summary>
    private static bool _undoRequested;
    private static readonly HandCostReductionTracker CostReductions = new();
    private static ActionExecutor? _costCheckExecutor;
    private static bool _checkingCosts;

    public static void Initialize()
    {
        CombatManager combat = CombatManager.Instance;
        combat.CombatSetUp += state => Guard(() => OnCombatSetUp(state));
        combat.CombatEnded += _ => Guard(OnCombatEnded);
        combat.PlayerUnendedTurn += player => Guard(() => OnPlayerUnendedTurn(player));
        combat.TurnStarted += _ => _undoRequested = false;
        combat.History.Changed += () => Guard(OnHistoryChanged);
    }
    
    /// <summary>購読中のプレイヤーごとのイベントハンドラ(次の戦闘で確実に解除するため)。</summary>
    private static readonly List<(Player Player, Action<int, int> Energy, Action<int, int> Stars, CardPile Hand, Action<CardModel> CardAdded, Action<CardModel> CardRemoved)> Subscriptions = new();

    private static void OnCombatSetUp(CombatState state)
    {
        ForcedTurnEndGuard.Clear();
        ResetHold();
        Unsubscribe();
        _player = null;

        // 自分(ローカルプレイヤー)の獲得だけを扱う。他プレイヤーの状態には一切触れない。
        foreach (Player player in state.Players)
        {
            if (!LocalContext.IsMe(player))
            {
                continue;
            }

            Player captured = player;
            Action<int, int> energy = (previous, current) =>
            {
                // 支払いで使用不可になった状態も、後の値下げとの比較元にする。
                Guard(CheckForCostReduction);
                if (previous < current)
                {
                    Guard(() => OnResourceGained(captured));
                }
            };
            CardPile hand = captured.PlayerCombatState!.Hand;
            Action<int, int> stars = (_, _) => Guard(CheckForCostReduction);
            Action<CardModel> cardAdded = card => Guard(() => OnHandCardAdded(captured, card));
            Action<CardModel> cardRemoved = _ => Guard(CheckForCostReduction);
            hand.CardAdded += cardAdded;
            hand.CardRemoved += cardRemoved;
            captured.PlayerCombatState.EnergyChanged += energy;
            captured.PlayerCombatState.StarsChanged += stars;
            Subscriptions.Add((captured, energy, stars, hand, cardAdded, cardRemoved));
            _player = player;
            break;
        }

        _costCheckExecutor = RunManager.Instance.ActionExecutor;
        _costCheckExecutor.BeforeActionExecuted += OnActionForCostCheck;
        _costCheckExecutor.AfterActionExecuted += OnActionForCostCheck;
        _processedHistoryEntries = CombatManager.Instance.History.Entries.Count();
    }

    private static void Unsubscribe()
    {
        if (_costCheckExecutor != null)
        {
            _costCheckExecutor.BeforeActionExecuted -= OnActionForCostCheck;
            _costCheckExecutor.AfterActionExecuted -= OnActionForCostCheck;
            _costCheckExecutor = null;
        }
        CostReductions.Clear();
        foreach (var (player, energy, stars, hand, cardAdded, cardRemoved) in Subscriptions)
        {
            hand.CardAdded -= cardAdded;
            hand.CardRemoved -= cardRemoved;
            if (player.PlayerCombatState != null)
            {
                player.PlayerCombatState.EnergyChanged -= energy;
                player.PlayerCombatState.StarsChanged -= stars;
            }
        }
        Subscriptions.Clear();
    }

    private static void OnPlayerUnendedTurn(Player player)
    {
        if (LocalContext.IsMe(player))
        {
            _undoRequested = false;
        }
    }

    private static void OnCombatEnded()
    {
        ForcedTurnEndGuard.Clear();
        Unsubscribe();
        ResetHold();
    }

    /// <summary>保留・予約の記録を捨てて、ターン終了を許可した状態に戻す(戦闘開始/終了時)。</summary>
    private static void ResetHold()
    {
        lock (TurnHoldLock)
        {
            _turnHoldCancellation?.Cancel();
            _turnHoldCancellation = null;
        }

        _canTurnEnd = true;
        _gainSequence = 0;
        _gainSequenceAtPress = null;
        _undoRequested = false;
    }

    /// <summary>
    /// ローカルプレイヤーがターン終了ボタンを押した(アクションを積んだ)ことを記録する。
    /// これ以降、予約が解決されるまでにリソースを獲得していたら、その予約を取り消す対象にする。
    /// </summary>
    public static void NotifyForcedEndTurn(Player player)
    {
        ForcedTurnEndGuard.Record(player);
        // 先行する通常終了予約の記録・保留タイマーを捨てる。Ready状態には触らない。
        ResetHold();
    }

    public static void NotifyEndTurnPressed()
    {
        if (_player == null || !CombatManager.Instance.IsPartOfPlayerTurn(_player) || ForcedTurnEndGuard.IsForced(_player) || CombatManager.Instance.IsPlayerReadyToEndTurn(_player))
        {
            // 既に終了予約済みなら、この押下は「取り消し」なので記録しない。
            return;
        }

        // 押す前に成立済みの値下げを、押した後の獲得と誤認しない。
        Guard(CheckForCostReduction);
        _gainSequenceAtPress = _gainSequence;
    }

    /// <summary>
    /// ターン終了予約が解決されようとしている今、その予約を無視すべきか。
    /// 押下時点から解決までの間にリソース獲得があったかで判断し、保留タイマーの残り時間には依存しない
    /// (獲得から解決まで保留時間以上かかっても取りこぼさない)。呼ぶと押下記録は消費される。
    /// </summary>
    public static bool ConsumeShouldCancelEndTurn()
    {
        // 再生アクションは手動の早押し予約ではない。再生前の保留も捨てる。
        if (RewindCompatibility.IsReplaying)
        {
            ResetHold();
            return false;
        }

        // 履歴を伴わない条件変更も、予約解決前に同期的に確認する。
        Guard(CheckForCostReduction);
        int? atPress = _gainSequenceAtPress;
        _gainSequenceAtPress = null;

        // ボタン経由でない終了(FTUE等)は押下記録が無いので、従来どおり保留中かどうかで判断する。
        return _player != null && CombatManager.Instance.IsPartOfPlayerTurn(_player) && !ForcedTurnEndGuard.IsForced(_player) && !CanSkipProtection && (atPress.HasValue ? atPress.Value != _gainSequence : !_canTurnEnd);
    }

    private static void OnHistoryChanged()
    {
        var entries = CombatManager.Instance.History.Entries;
        var count = entries.Count();
        if (count < _processedHistoryEntries)
        {
            // 履歴が何らかの処理によりClear()されているので読み直す
            _processedHistoryEntries = 0;
        }

        // 今まで読んだ履歴の数をカウントしてスキップすることで、最新の履歴だけを読む。
        var freshEntries = entries.Skip(_processedHistoryEntries).ToList();
        _processedHistoryEntries = count;

        foreach (CombatHistoryEntry entry in freshEntries)
        {
            switch (entry)
            {
                case CardDrawnEntry { FromHandDraw: false } drawn:
                    OnCardDrawn(drawn);
                    break;
                case PotionUsedEntry used:
                    OnPotionUsed(used);
                    break;
            }
        }
        CheckForCostReduction();
    }

    private static void OnActionForCostCheck(GameAction _) => Guard(CheckForCostReduction);

    /// <summary>Play中の値下げによる使用可能化を、既存の獲得・解除経路へ合流させる。</summary>
    private static void CheckForCostReduction()
    {
        if (_checkingCosts)
            return;
        _checkingCosts = true;
        try
        {
            Player? player = _player;
            CombatManager combat = CombatManager.Instance;
            if (player == null || !combat.IsInProgress || !combat.IsPartOfPlayerTurn(player)
                || player.PlayerCombatState?.Phase != PlayerTurnPhase.Play
                || player.Creature.IsDead || ForcedTurnEndGuard.IsForced(player) || RewindCompatibility.IsReplaying)
            {
                CostReductions.Clear();
                return;
            }
            if (CostReductions.Observe(player))
                OnResourceGained(player);
        }
        finally
        {
            _checkingCosts = false;
        }
    }

    /// <summary>プレイ中の手札追加を検知する。生成・回収・追加ドローを同じ経路で扱う。</summary>
    private static void OnHandCardAdded(Player player, CardModel card)
    {
        if (!CombatManager.Instance.IsInProgress
            || player.Creature.CombatState?.CurrentSide != CombatSide.Player
            || player.PlayerCombatState?.Phase != PlayerTurnPhase.Play
            || !LocalContext.IsMe(card.Owner))
            return;

        CheckForCostReduction();
        OnResourceGained(player);
    }

    // プレイ中のドローはCardAddedで検知済み。開始時の通常配札は履歴側でも除外される。
    private static void OnCardDrawn(CardDrawnEntry entry)
    {
        if (LocalContext.IsMe(entry.Card.Owner) && entry.Card.Owner.PlayerCombatState?.Phase != PlayerTurnPhase.Play)
        {
            OnResourceGained(entry.Card.Owner);
        }
    }

    /// <summary>
    /// 他のプレイヤーが使ったポーションの対象が自分だった(=自分がポーションの影響を受けた)。
    /// 設定で有効なときだけ、追加リソース獲得と同じ扱いにする。
    /// </summary>
    private static void OnPotionUsed(PotionUsedEntry entry)
    {
        if (!HoldYourTurnConfig.PotionInterruptsTurnEnd)
        {
            return;
        }

        Player? target = entry.Target?.Player;
        if (target != null && LocalContext.IsMe(target) && !LocalContext.IsMe(entry.Potion.Owner))
        {
            OnResourceGained(target);
        }
    }

    /// <summary>
    /// 追加リソース(手札追加/エネルギー増加、設定が有効なら他プレイヤーのポーションの影響)をプレイヤーが獲得した。
    /// ローカルプレイヤーの獲得だけを扱う。解除は正規アクションで配信する。
    /// </summary>
    private static void OnResourceGained(Player player)
    {
        if (!CombatManager.Instance.IsPartOfPlayerTurn(player) || ForcedTurnEndGuard.IsForced(player))
            return;

        // 再生で復元されたリソースは、手動プレイでの新規獲得ではない。
        if (RewindCompatibility.IsReplaying)
        {
            ResetHold();
            return;
        }

        if (CanSkipProtection)
        {
            // 獲得記録は維持し、今不要な待ち時間だけを解除する。
            _gainSequence++;
            lock (TurnHoldLock)
            {
                _turnHoldCancellation?.Cancel();
                _turnHoldCancellation = null;
                EnableTurnEnd(true);
            }
            return;
        }

        PreventImmediateTurnEnd();
        RequestUndoEndTurnOnGain(player);
    }

    /// <summary>
    /// マルチプレイで、ターン終了済み(ただし全員は未完了)の自分が追加リソースを得たら、自分の意思で
    /// ターン終了を解除する。<c>UndoReadyToEndTurn</c>を直接呼ばず、UIの「取り消す」ボタンと同じ
    /// <see cref="UndoEndPlayerTurnAction"/>を<c>RequestEnqueue</c>し、全ピアに正規の経路で配る。
    /// 判断はローカルだが、状態を変えるのはゲームアクションだけなので、modを持たないピアとも同期がずれない。
    /// </summary>
    private static void RequestUndoEndTurnOnGain(Player player)
    {
        if (!CombatManager.Instance.IsPartOfPlayerTurn(player) || ForcedTurnEndGuard.IsForced(player) || _undoRequested || RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
        {
            return;
        }

        CombatManager combat = CombatManager.Instance;
        if (!combat.IsInProgress
            || !combat.IsPlayerReadyToEndTurn(player)
            || combat.AllPlayersReadyToEndTurn() // 全員終了済みならターン終了処理に入っている。巻き戻さない。
            || player.Creature.CombatState?.CurrentSide != CombatSide.Player)
        {
            return;
        }

        _undoRequested = true;
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
            new UndoEndPlayerTurnAction(player, player.PlayerCombatState!.TurnNumber));
    }

    private static void PreventImmediateTurnEnd()
    {
        _gainSequence++;

        CancellationTokenSource cancellation;
        lock (TurnHoldLock)
        {
            _turnHoldCancellation?.Cancel();
            cancellation = new CancellationTokenSource();
            _turnHoldCancellation = cancellation;
            EnableTurnEnd(false);
        }

        _ = EnableTurnEndAfterHold(cancellation);
    }

    private static async Task EnableTurnEndAfterHold(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(HoldYourTurnConfig.TurnHoldTime), cancellation.Token);

            lock (TurnHoldLock)
            {
                if (!ReferenceEquals(_turnHoldCancellation, cancellation))
                {
                    return;
                }

                _turnHoldCancellation = null;
                EnableTurnEnd(true);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"{MainFile.ModId}: failed to release turn end after hold: {e}");
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static void EnableTurnEnd(bool enable)
    {
        CanTurnEnd = enable;

        // UI更新はGodotのメインスレッドで行う(Task.Delayの継続がどのスレッドで走っても安全にする)。
        Callable.From(RefreshEndTurnButton).CallDeferred();
    }

    private static void RefreshEndTurnButton()
    {
        // 戦闘終了後などはルームが無いことがある。
        NCombatRoom.Instance?.Ui?.EndTurnButton?.RefreshEnabled();
    }

    private static void Guard(Action handler)
    {
        try
        {
            handler();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"{MainFile.ModId}: unhandled exception in chatter hub: {e}");
        }
    }
}