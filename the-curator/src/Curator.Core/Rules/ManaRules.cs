using Curator.Core.Content;
using Curator.Core.Game;

namespace Curator.Core.Rules;

/// <summary>The morning mana grant (BUILD_BRIEF §5.2).</summary>
public static class ManaRules
{
    /// <summary>Computes the grant for a morning, from the state as it stands before the grant.</summary>
    /// <param name="content">The content.</param>
    /// <param name="state">The state.</param>
    /// <param name="day">The day being started.</param>
    /// <returns>The grant.</returns>
    public static ManaGrant MorningGrant(ContentSet content, GameState state, int day)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        if (content.Day(day).StartMana is { } fixedMana)
        {
            return new ManaGrant(fixedMana, fixedMana, 0, 0, 0, Fixed: true);
        }

        var mana = content.Balance.Mana;
        var rollover = Math.Min(Math.Max(state.Mana, 0), mana.RolloverCap);
        var attentive = Math.Min(state.LastAttentive, mana.AttentiveBonusCap);
        var good = state.GoodSurfacedSinceGrant * mana.GoodOutcomeBonus;
        var bonus = Math.Min(attentive + good, mana.TotalBonusCap);
        var attentivePart = Math.Min(attentive, bonus);
        return new ManaGrant(mana.BasePerDay + bonus + rollover, mana.BasePerDay, attentivePart, bonus - attentivePart, rollover, Fixed: false);
    }
}
