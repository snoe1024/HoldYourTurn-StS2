using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace HoldYourTurn.HoldYourTurnCode;

/// <summary>解除APIを起動時に選び、実行中は取得済みのデリゲートだけを呼ぶ。</summary>
internal static class CombatTurnCompatibility
{
    private static Action<CombatManager, Player>? _undoReadyToEndTurn;

    public static void Initialize()
    {
        if (_undoReadyToEndTurn != null)
            return;

        // 新APIが持つ強制終了・追加ターン・死亡の拒否条件をそのまま利用する。
        // 片方の版にしかないメソッドを直接参照すると、未実行分岐でもJITに失敗しうる。
        foreach (string name in new[] { "TryUndoReadyToEndTurn", "UndoReadyToEndTurn" })
        {
            MethodInfo? method = typeof(CombatManager).GetMethod(name,
                BindingFlags.Public | BindingFlags.Instance, binder: null,
                types: new[] { typeof(Player) }, modifiers: null);
            if (method == null || method.ReturnType != typeof(void) || method.ContainsGenericParameters)
                continue;

            _undoReadyToEndTurn = method.CreateDelegate<Action<CombatManager, Player>>();
            return;
        }

        // 必須機能。解除に失敗したまま本体の終了処理だけをスキップしない。
        throw new MissingMethodException(typeof(CombatManager).FullName,
            "TryUndoReadyToEndTurn(Player) or UndoReadyToEndTurn(Player)");
    }

    public static void UndoReadyToEndTurn(CombatManager combat, Player player)
    {
        var undo = _undoReadyToEndTurn
            ?? throw new InvalidOperationException("HoldYourTurn turn-end compatibility was not initialized.");
        undo(combat, player);
    }
}
