namespace HoldYourTurn.HoldYourTurnCode;

internal enum PotionOpportunityKind
{
    None,
    Energy,
    Cards,
    ExhaustCards,
    Review,
    CalmExitEnergy,
    EnergyVariable,
    StarsVariable,
    DynamicVariables,
    HandRequiredCards,
    PowerRequiredCards,
    DiscardCards,
    UpgradeHand,
    UpgradeDeckHand,
    XCost,
    StasisCards,
    CostReduction
}

/// <summary>分類済み効果と現在の手札・コスト要因を組み合わせる、副作用のない軽量判定。</summary>
internal static class PotionOpportunityPolicy
{
    public static bool CanHelp(PotionOpportunityKind kind, bool energyBlocked, bool starsBlocked,
        bool hasHand, bool hasDiscard, bool hasExhaust, bool positiveEnergy, bool positiveStars, bool hasPower = false)
    {
        return kind switch
        {
            PotionOpportunityKind.None => false,
            PotionOpportunityKind.Energy => energyBlocked,
            PotionOpportunityKind.Cards => true,
            PotionOpportunityKind.ExhaustCards => hasExhaust,
            PotionOpportunityKind.DiscardCards => hasDiscard,
            PotionOpportunityKind.HandRequiredCards => hasHand,
            PotionOpportunityKind.PowerRequiredCards => hasPower,
            PotionOpportunityKind.CostReduction => hasHand && (energyBlocked || starsBlocked),
            PotionOpportunityKind.EnergyVariable => energyBlocked && positiveEnergy,
            PotionOpportunityKind.StarsVariable => starsBlocked && positiveStars,
            PotionOpportunityKind.DynamicVariables => (energyBlocked && positiveEnergy) || (starsBlocked && positiveStars),
            // 未承認の間接効果は「対応済み」とせず、従来のunknown扱いを維持する。
            _ => true
        };
    }
}
