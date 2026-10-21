using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;

namespace HoldYourTurn.HoldYourTurnCode;

/// <summary>
/// 長いカード解決の途中で早まって押したターン終了予約が、リソース獲得(追加ドロー/エネルギー)の後に解決された場合に、
/// その予約を無視してターン終了を解除する。
/// <para>
/// シングルプレイ: 予約をその場で握りつぶす(状態を持つのは自分だけなので同期の問題は無い)。
/// マルチプレイ: <c>SetReadyToEndTurn</c>は全ピアで同じ結果にならなければならないので、実行時には何も変えない。
/// 通常どおり予約させた後に、正規の<see cref="UndoEndPlayerTurnAction"/>をActionQueue経由で積み、全ピアに同じ解除を配る。
/// </para>
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
internal static class SetReadyToEndTurnPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        CombatManager __instance,
        Player player,
        bool canBackOut,
        out bool __state)
    {
        __state = false;

        // 終了後のリソース獲得でも解除しないよう、強制終了を記録する。
        if (!canBackOut && LocalContext.IsMe(player))
            TurnHolder.NotifyForcedEndTurn(player);

        // 強制・不可逆の終了はバニラに任せる。他プレイヤー分は絶対に触らない。
        if (!canBackOut || !LocalContext.IsMe(player) || !__instance.IsPartOfPlayerTurn(player))
        {
            return true;
        }

        if (!TurnHolder.ConsumeShouldCancelEndTurn())
        {
            return true;
        }

        if (RunManager.Instance.NetService.Type != NetGameType.Singleplayer)
        {
            // マルチプレイでは実行をスキップしない。Postfixで正規の解除アクションを積む。
            __state = true;
            return true;
        }

        // SetReadyToEndTurnを実行せず、UI側にも「ターンを戻した」通知を通す。
        __instance.UndoReadyToEndTurn(player);
        return false;
    }

    [HarmonyPostfix]
    private static void Postfix(Player player, bool __state)
    {
        if (!__state || !CombatManager.Instance.IsPartOfPlayerTurn(player) || ForcedTurnEndGuard.IsForced(player) || !IsReady(player))
        {
            return;
        }

        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
            new UndoEndPlayerTurnAction(player, player.PlayerCombatState!.TurnNumber));
    }

    private static bool IsReady(Player player)
        => CombatManager.Instance.IsPlayerReadyToEndTurn(player);
}
