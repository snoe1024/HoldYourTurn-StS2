using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;

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

            if (!player.CanUseOrRemovePotions)
                return false;

            foreach (PotionModel potion in player.Potions)
            {
                if (potion.Usage is not (PotionUsage.CombatOnly or PotionUsage.AnyTime))
                    continue;
                // 未知のmod製ポーションは変数だけでは全効果を証明できない。
                if (potion.GetType().Assembly != typeof(PotionModel).Assembly)
                    return true;
                if (potion is SwiftPotion or CureAll or BottledPotential
                    or DropletOfPrecognition or LiquidMemories or PotOfGhouls or GlowwaterPotion
                    || (potion is GamblersBrew && state.Hand.Cards.Count > 0))
                    return true;
                if ((energyBlocked || starsBlocked) && potion is TouchOfInsanity)
                    return true;
                bool canTargetSelf = potion.TargetType is TargetType.Self or TargetType.AnyPlayer;
                if (canTargetSelf && potion.DynamicVars.Values.Any(v => v.BaseValue > 0
                    && ((energyBlocked && v is EnergyVar) || (starsBlocked && v is StarsVar))))
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
