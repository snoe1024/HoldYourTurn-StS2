using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HoldYourTurn.HoldYourTurnCode;

internal static class TurnHolder
{
    private static readonly Lock TurnHoldLock = new();
    private static CancellationTokenSource? _turnHoldCancellation;

    public static bool CanPressTurnEnd => CombatManager.Instance.IsPlayerReadyToEndTurn(_player) || CanTurnEnd;

    private static bool _canTurnEnd;
    public static bool CanTurnEnd
    {
        get => _canTurnEnd;
        private set => _canTurnEnd = value;
    }
    
    /// <summary>直前までに処理済みの<c>CombatHistory</c>のエントリ数(新規エントリだけを拾うため)。</summary>
    private static int _processedHistoryEntries;

    private static Player _player;
    
    public static void Initialize()
    {
        CombatManager combat = CombatManager.Instance;
        combat.CombatSetUp += state => Guard(() => OnCombatSetUp(state));
        combat.History.Changed += () => Guard(OnHistoryChanged);
    }
    
    private static void OnCombatSetUp(CombatState state)
    {
        _canTurnEnd = true;
        
        foreach (Player player in state.Players)
        {
            // 視点主についてだけ考える
            if (!LocalContext.IsMe(player))
            {
                continue;
            }
            
            _player = player;
            player.PlayerCombatState.EnergyChanged += OnEnergyChanged;
            
            break;
        }

        _processedHistoryEntries = CombatManager.Instance.History.Entries.Count();
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
            }
        }
    }

    private static void OnCardDrawn(CardDrawnEntry entry)
    {
        if (!LocalContext.IsMe(entry.Card.Owner))
        {
            return;
        }

        PreventImmediateTurnEnd();
    }

    private static void OnEnergyChanged(int previous, int current)
    {
        if (previous >= current) return;

        PreventImmediateTurnEnd();
    }

    private static void PreventImmediateTurnEnd()
    {
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
        
        // UI更新処理を呼び出す
        NCombatRoom.Instance.Ui.EndTurnButton.RefreshEnabled();
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