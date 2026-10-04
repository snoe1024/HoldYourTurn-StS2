namespace HoldYourTurn.HoldYourTurnCode;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

[HarmonyPatch(typeof(NEndTurnButton), nameof(NEndTurnButton.RefreshEnabled))]
internal static class EndTurnRefreshPatch
{
    [HarmonyPostfix]
    private static void Postfix(NEndTurnButton __instance)
    {
        // バニラが有効と判断した場合にだけ、追加条件でさらに無効化する。
        if (!TurnHolder.CanPressTurnEnd)
            __instance.Disable();
    }
}

[HarmonyPatch(typeof(NEndTurnButton), nameof(NEndTurnButton.CallReleaseLogic))]
internal static class EndTurnReleasePatch
{
    [HarmonyPrefix]
    private static bool Prefix(NEndTurnButton __instance)
    {
        // Disable後の直接呼び出しも止め、バニラの有効状態とmod条件をANDする。
        if (!__instance.IsEnabled || !TurnHolder.CanPressTurnEnd)
            return false;

        // 押した時点を記録し、予約が解決される時にリソース獲得があったかを判定できるようにする。
        TurnHolder.NotifyEndTurnPressed();
        return true;
    }
}