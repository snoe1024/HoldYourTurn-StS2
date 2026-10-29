using System.Collections.Concurrent;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace HoldYourTurn.HoldYourTurnCode;

/// <summary>
/// ポーションを実行せず、現在のリソース獲得を活かせる効果だけを調べる。
/// 型の分類はキャッシュする。modのIL解析やポーションのインスタンス生成は行わない。
/// </summary>
internal static class PotionOpportunityClassifier
{
    private static readonly ConcurrentDictionary<Type, PotionOpportunityKind> Cache = new();
    private static readonly Dictionary<(string Assembly, string Type), PotionOpportunityKind> Profiles = LoadProfiles();

    public static bool CanHelp(PotionModel potion, Player player, bool energyBlocked, bool starsBlocked)
    {
        if (potion.Usage is not (PotionUsage.CombatOnly or PotionUsage.AnyTime))
            return false;

        if (!potion.PassesCustomUsabilityCheck)
            return false;

        PotionOpportunityKind kind = Cache.GetOrAdd(potion.GetType(), ClassifyType);
        if (kind == PotionOpportunityKind.CalmExitEnergy)
            return energyBlocked && WatcherCalmReader.CanGainEnergy(potion, player);

        if (kind is PotionOpportunityKind.UpgradeHand or PotionOpportunityKind.UpgradeDeckHand
            or PotionOpportunityKind.XCost or PotionOpportunityKind.StasisCards)
            return IndirectPotionEvaluator.CanHelp(kind, potion, player);

        var state = player.PlayerCombatState;
        if (state == null)
            return true;

        bool hasEnergy = false;
        bool hasStars = false;
        if (kind is PotionOpportunityKind.EnergyVariable or PotionOpportunityKind.StarsVariable
            or PotionOpportunityKind.DynamicVariables)
        {
            bool targetsSelf = potion.TargetType is MegaCrit.Sts2.Core.Entities.Cards.TargetType.Self
                or MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyPlayer;
            if (targetsSelf)
            {
                foreach (DynamicVar value in potion.DynamicVars.Values)
                {
                    if (value.BaseValue <= 0)
                        continue;
                    hasEnergy |= value is EnergyVar;
                    hasStars |= value is StarsVar;
                }
            }
        }

        return PotionOpportunityPolicy.CanHelp(kind, energyBlocked, starsBlocked,
            state.Hand.Cards.Count > 0, state.DiscardPile.Cards.Count > 0,
            state.ExhaustPile.Cards.Count > 0, hasEnergy, hasStars,
            kind == PotionOpportunityKind.PowerRequiredCards
                && state.Hand.Cards.Any(card => card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Power));
    }

    private static PotionOpportunityKind ClassifyType(Type type)
    {
        if (type.Assembly == typeof(PotionModel).Assembly)
        {
            return Profiles.TryGetValue(("$vanilla", type.FullName ?? ""), out var vanilla)
                ? vanilla : PotionOpportunityKind.DynamicVariables;
        }
        return ResolveModProfile(type.Assembly.GetName().Name ?? "", type.FullName ?? "");
    }

    internal static PotionOpportunityKind ResolveModProfile(string assembly, string type)
    {
        if (Profiles.TryGetValue((assembly, type), out var profile))
            return profile;

        // 型名が未登録の場合だけ、従来の安全側の挙動を維持する。
        return PotionOpportunityKind.Review;
    }

    private static Dictionary<(string, string), PotionOpportunityKind> LoadProfiles()
    {
        using Stream stream = typeof(PotionOpportunityClassifier).Assembly
            .GetManifestResourceStream("HoldYourTurn.PotionProfiles")
            ?? throw new InvalidOperationException("Missing embedded potion profiles.");
        using JsonDocument document = JsonDocument.Parse(stream);
        var profiles = new Dictionary<(string, string), PotionOpportunityKind>();
        foreach (JsonElement assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            string name = assembly.GetProperty("assembly").GetString()!;
            foreach (JsonProperty group in assembly.GetProperty("groups").EnumerateObject())
            {
                var kind = Enum.Parse<PotionOpportunityKind>(group.Name);
                foreach (JsonElement type in group.Value.EnumerateArray())
                    profiles.Add((name, type.GetString()!), kind);
            }
        }
        return profiles;
    }
}
