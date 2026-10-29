using System.Collections.Concurrent;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace HoldYourTurn.HoldYourTurnCode;

/// <summary>
/// 追加のmod DLLを参照せず、限定した間接効果だけを試算する。
/// 未知のカード・取得失敗・API変更では防止を維持する。ポーションのOnUseは呼ばない。
/// </summary>
internal static class IndirectPotionEvaluator
{
    private static readonly ConcurrentDictionary<Assembly, Lazy<Func<Player, IReadOnlyList<CardModel>>?>> StasisReaders = new();
    private static readonly ConcurrentDictionary<Type, byte> ReportedFailures = new();

    public static bool CanHelp(PotionOpportunityKind kind, PotionModel potion, Player player)
    {
        try
        {
            return kind switch
            {
                PotionOpportunityKind.UpgradeHand => UpgradeCanHelp(player, deckOnly: false),
                PotionOpportunityKind.UpgradeDeckHand => UpgradeCanHelp(player, deckOnly: true),
                PotionOpportunityKind.XCost => XCostCanHelp(player),
                PotionOpportunityKind.StasisCards => StasisCanHelp(potion.GetType().Assembly, player),
                _ => true
            };
        }
        catch (Exception e)
        {
            if (ReportedFailures.TryAdd(potion.GetType(), 0))
                MainFile.Logger.Error($"HoldYourTurn: indirect potion check unavailable; retaining protection ({potion.GetType().FullName}): {e}");
            return true;
        }
    }

    private static bool UpgradeCanHelp(Player player, bool deckOnly)
    {
        var state = player.PlayerCombatState;
        if (state == null)
            return true;

        foreach (CardModel card in state.Hand.Cards)
        {
            if (!card.IsUpgradable || (deckOnly && (card.DeckVersion == null || !card.DeckVersion.IsUpgradable)))
                continue;

            // mod製のOnUpgradeや加工効果を試算のために実行しない。
            if (card.GetType().Assembly != typeof(CardModel).Assembly
                || card.Enchantment != null || card.Affliction != null || !card.IsMutable)
                return true;

            // CardScope/CreateClone/CardCmd.Upgradeは使用しない（戦闘登録やフックを避ける）。
            // バニラのMutableCloneはコストとDynamicVarsを独立化し、イベント購読を解除する。
            var preview = (CardModel)card.ClonePreservingMutability();
            if (ReferenceEquals(preview, card))
                return true;
            preview.UpgradeInternal();
            preview.FinalizeUpgradeInternal();
            if (preview.CanPlay())
                return true;
        }
        return false;
    }

    private static bool XCostCanHelp(Player player)
    {
        var state = player.PlayerCombatState;
        if (state == null)
            return true;

        foreach (CardModel card in state.Hand.Cards)
        {
            if (card.EnergyCost.CostsX)
                continue;
            if (card.CanPlay(out UnplayableReason reason, out _))
                return true;
            // X化で改善するのはエナジー不足だけ。他の使用不可理由が残るなら数えない。
            if (reason == UnplayableReason.EnergyCostTooHigh)
                return true;
        }
        return false;
    }

    private static bool StasisCanHelp(Assembly assembly, Player player)
    {
        // 実際に所有する調査済みポーションのDLLだけを調べる。mod未導入時には到達しない。
        var reader = StasisReaders.GetOrAdd(assembly, static a => new Lazy<Func<Player, IReadOnlyList<CardModel>>?>(() =>
        {
            Type? type = a.GetType("Guardian.GuardianCode.Extensions.PlayerExtensions", throwOnError: false);
            MethodInfo? method = type?.GetMethod("get_StasisPile", BindingFlags.Public | BindingFlags.Static,
                binder: null, types: new[] { typeof(Player) }, modifiers: null);
            if (method == null || method.ReturnType != typeof(IReadOnlyList<CardModel>))
                return null;
            return method.CreateDelegate<Func<Player, IReadOnlyList<CardModel>>>();
        })).Value;

        // GetterはCustomPilesの既存カード一覧を返すだけ。加速/退出/カード移動は実行しない。
        return reader == null || reader(player).Count > 0;
    }
}
