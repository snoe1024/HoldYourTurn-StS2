using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;

namespace HoldYourTurn.HoldYourTurnCode;

/// <summary>旧通常版と新ベータのポーション使用制限getterを起動時に選ぶ。</summary>
internal static class PotionUseCompatibility
{
    private static Func<Player, bool>? _canUsePotions;

    public static void Initialize()
    {
        if (_canUsePotions != null)
            return;

        foreach (string name in new[] { "CanUseOrRemovePotions", "CanRemovePotions" })
        {
            MethodInfo? getter = typeof(Player).GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance)?.GetMethod;
            if (getter == null || getter.ReturnType != typeof(bool) || getter.GetParameters().Length != 0)
                continue;

            _canUsePotions = getter.CreateDelegate<Func<Player, bool>>();
            return;
        }

        throw new MissingMemberException(typeof(Player).FullName,
            "CanUseOrRemovePotions or CanRemovePotions");
    }

    public static bool CanUsePotions(Player player)
    {
        var canUse = _canUsePotions
            ?? throw new InvalidOperationException("HoldYourTurn potion compatibility was not initialized.");
        return canUse(player);
    }
}
