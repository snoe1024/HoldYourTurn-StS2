using MegaCrit.Sts2.Core.Entities.Players;

namespace HoldYourTurn.HoldYourTurnCode;

/// <summary>canBackOut:falseを記録する。別戦闘・次ターンには持ち越さない。</summary>
internal static class ForcedTurnEndGuard
{
    private static Player? _player;
    private static object? _combatState;
    private static int _turnNumber;

    public static void Record(Player player)
    {
        if (player.PlayerCombatState == null)
            return;
        _player = player;
        _combatState = player.PlayerCombatState;
        _turnNumber = player.PlayerCombatState.TurnNumber;
    }

    public static bool IsForced(Player player) => ReferenceEquals(_player, player)
        && _combatState != null && ReferenceEquals(_combatState, player.PlayerCombatState)
        && player.PlayerCombatState?.TurnNumber == _turnNumber;

    public static void Clear()
    {
        _player = null;
        _combatState = null;
        _turnNumber = 0;
    }
}
