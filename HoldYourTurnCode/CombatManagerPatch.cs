using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;

namespace HoldYourTurn.HoldYourTurnCode;

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
internal static class SetReadyToEndTurnPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        CombatManager __instance,
        Player player,
        bool canBackOut)
    {
        // 強制・不可逆の終了はバニラに任せ、イベントによる取消し対象にしない。
        if (!canBackOut)
        {
            return true;
        }
        
        // 即断防止はこのクライアントのプレイヤーにだけ適用する。
        // 他プレイヤーのターン終了通知を止めると、マルチプレイ状態が同期しなくなる。
        if (!LocalContext.IsMe(player) || TurnHolder.CanTurnEnd)
            return true;

        // SetReadyToEndTurnを実行せず、UI側にも「ターンを戻した」通知を通す。
        __instance.UndoReadyToEndTurn(player);
        return false;
    }
}