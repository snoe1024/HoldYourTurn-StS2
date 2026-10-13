using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;

namespace HoldYourTurn.HoldYourTurnCode;

/// <summary>Rewindは任意依存。起動時に一度だけ探索し、ゲーム中は取得済みのデリゲートを使う。</summary>
internal static class RewindCompatibility
{
    private static Func<bool>? _isReplaying;
    private static bool _initialized;
    private static bool _reportedFailure;

    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        try
        {
            Type? type = AccessTools.TypeByName("Rewind.Scripts.TurnRewindManager");
            MethodInfo? getter = type?.GetProperty("IsReplaying", BindingFlags.Public | BindingFlags.Static)?.GetMethod;
            if (getter != null && getter.ReturnType == typeof(bool))
                _isReplaying = getter.CreateDelegate<Func<bool>>();
        }
        catch (Exception e)
        {
            ReportFailure(e);
        }
    }

    public static bool IsReplaying
    {
        get
        {
            try
            {
                return _isReplaying?.Invoke() ?? false;
            }
            catch (Exception e)
            {
                ReportFailure(e);
                return false;
            }
        }
    }

    private static void ReportFailure(Exception e)
    {
        if (_reportedFailure)
            return;
        _reportedFailure = true;
        MainFile.Logger.Error($"HoldYourTurn: unable to read Rewind replay state: {e}");
    }
}

/// <summary>全modのロード完了後、メインメニュー表示前に互換情報を確定する。</summary>
[HarmonyPatch(typeof(OneTimeInitialization), nameof(OneTimeInitialization.ExecuteEssential))]
internal static class RewindCompatibilityInitializationPatch
{
    [HarmonyPostfix]
    private static void Postfix() => RewindCompatibility.Initialize();
}
