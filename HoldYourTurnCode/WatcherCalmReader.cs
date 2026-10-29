using System.Collections.Concurrent;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace HoldYourTurn.HoldYourTurnCode;

/// <summary>両WatcherのCalm状態だけを読み取る。未導入・API変更時は判定不能として防止を維持する。</summary>
internal static class WatcherCalmReader
{
    private static readonly ConcurrentDictionary<Assembly, Lazy<Func<Player, bool>?>> Readers = new();
    private static readonly ConcurrentDictionary<Assembly, byte> ReportedFailures = new();

    public static bool CanGainEnergy(PotionModel potion, Player player)
    {
        Assembly assembly = potion.GetType().Assembly;
        try
        {
            var reader = Readers.GetOrAdd(assembly, static a => new Lazy<Func<Player, bool>?>(() =>
            {
                // Boninall版: CalmスタンスはCreatureのPowerで保持される。
                Type? calmPower = a.GetType("WatcherMod.Calm", throwOnError: false);
                if (calmPower != null)
                    return player => player.Creature.Powers.Any(power => power.GetType() == calmPower);

                // lamali版: 公開getterで現在のスタンスを読む。スタンス変更や効果は実行しない。
                Type? calmStance = a.GetType("Watcher.Code.Stances.CalmStance", throwOnError: false);
                Type? model = a.GetType("Watcher.Code.Core.WatcherModel", throwOnError: false);
                MethodInfo? method = model?.GetMethod("GetStanceModel", BindingFlags.Public | BindingFlags.Static,
                    binder: null, types: new[] { typeof(Player) }, modifiers: null);
                if (calmStance == null || method == null || !typeof(AbstractModel).IsAssignableFrom(method.ReturnType))
                    return null;
                var getStance = method.CreateDelegate<Func<Player, AbstractModel>>();
                return player => getStance(player)?.GetType() == calmStance;
            })).Value;

            return reader == null || reader(player);
        }
        catch (Exception e)
        {
            if (ReportedFailures.TryAdd(assembly, 0))
                MainFile.Logger.Error($"HoldYourTurn: Watcher Calm state unavailable; retaining protection: {e}");
            return true;
        }
    }
}
