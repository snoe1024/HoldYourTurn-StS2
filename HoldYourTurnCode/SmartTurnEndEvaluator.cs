using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace HoldYourTurn.HoldYourTurnCode;

/// <summary>手札とポーションを読み取り専用で調べる。行動の可能性や不明点があれば防止を維持する。</summary>
internal static class SmartTurnEndEvaluator
{
    public static bool ShouldProtectTurnEnd(Player player)
    {
        try
        {
            var state = player.PlayerCombatState;
            if (state == null || player.Creature.CombatState?.CurrentSide != CombatSide.Player
                || state.Phase != PlayerTurnPhase.Play)
                return true;

            bool energyBlocked = false;
            bool starsBlocked = false;
            foreach (CardModel card in state.Hand.Cards)
            {
                if (card.CanPlay(out UnplayableReason reason, out _))
                    return true;
                energyBlocked |= (reason & UnplayableReason.EnergyCostTooHigh) != 0;
                starsBlocked |= (reason & UnplayableReason.StarCostTooHigh) != 0;
            }

            if (!PotionUseCompatibility.CanUsePotions(player))
                return false;

            foreach (PotionModel potion in player.Potions)
            {
                if (PotionOpportunityClassifier.CanHelp(potion, player, energyBlocked, starsBlocked))
                    return true;
            }
            return false;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"HoldYourTurn: smart-mode evaluation failed; retaining protection: {e}");
            return true;
        }
    }
}
