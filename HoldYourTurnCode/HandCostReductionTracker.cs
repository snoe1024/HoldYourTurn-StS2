using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace HoldYourTurn.HoldYourTurnCode;

/// <summary>同じ手札カードがコスト不足から値下げで使用可能になった変化だけを検出する。</summary>
internal sealed class HandCostReductionTracker
{
    private readonly record struct Snapshot(bool CanPlay, UnplayableReason Reason, int Energy, int Stars);
    private Dictionary<CardModel, Snapshot> _previous = new(ReferenceEqualityComparer.Instance);
    private object? _combatState;
    private int _turnNumber;

    public void Clear()
    {
        _previous.Clear();
        _combatState = null;
        _turnNumber = 0;
    }

    public bool Observe(Player player)
    {
        var state = player.PlayerCombatState;
        if (state == null)
        {
            Clear();
            return false;
        }
        if (!ReferenceEquals(_combatState, state) || _turnNumber != state.TurnNumber)
        {
            Clear();
            _combatState = state;
            _turnNumber = state.TurnNumber;
        }

        bool improved = false;
        var next = new Dictionary<CardModel, Snapshot>(ReferenceEqualityComparer.Instance);
        foreach (CardModel card in state.Hand.Cards)
        {
            // 他modの判定失敗は追加検知だけを見送る。次回の比較元にも使わない。
            Snapshot current;
            try
            {
                bool canPlay = card.CanPlay(out UnplayableReason reason, out _);
                current = new Snapshot(canPlay, reason,
                    Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.All)),
                    Math.Max(0, card.GetStarCostWithModifiers()));
            }
            catch (Exception)
            {
                continue;
            }

            if (_previous.TryGetValue(card, out Snapshot before) && !before.CanPlay && current.CanPlay)
            {
                bool energyImproved = (before.Reason & UnplayableReason.EnergyCostTooHigh) != 0
                    && current.Energy < before.Energy;
                bool starsImproved = (before.Reason & UnplayableReason.StarCostTooHigh) != 0
                    && current.Stars < before.Stars;
                improved |= energyImproved || starsImproved;
            }
            next[card] = current;
        }
        _previous = next;
        return improved;
    }
}
