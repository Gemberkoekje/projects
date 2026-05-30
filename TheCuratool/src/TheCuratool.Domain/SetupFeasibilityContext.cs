namespace TheCuratool.Domain;

/// <summary>
/// Provides the draft-state information needed to decide whether an enumerated target
/// <see cref="SetupCounts"/> distribution is still reachable by a complete legal draft.
/// <para>
/// Used by the setup calculator to prune target distributions that no legal sequence of
/// remaining picks could ever complete (so the board cannot be painted into a corner).
/// </para>
/// <para>
/// When <see cref="Enforce"/> is <see langword="false"/> the context imposes no constraints and
/// every candidate distribution is kept. This is the graceful-degradation state used by callers
/// that do not supply draft state (e.g. direct calculator callers) and is equivalent to the
/// pre-draft state where no picks have been made and all seats remain.
/// </para>
/// </summary>
/// <param name="Enforce">Whether reachability pruning is applied. When <see langword="false"/>, all candidates are kept.</param>
/// <param name="CurrentCounts">The per-type counts already locked in by recorded picks and Storyteller-assigned slots.</param>
/// <param name="RemainingSeats">The number of seats still to be filled in the draft.</param>
/// <param name="AvailableTownsfolk">Townsfolk characters still available on the script (not chosen, not consumed, not borrowed).</param>
/// <param name="AvailableOutsiders">Outsider characters still available on the script.</param>
/// <param name="AvailableMinions">Minion characters still available on the script.</param>
/// <param name="AvailableDemons">Demon characters still available on the script.</param>
public sealed record SetupFeasibilityContext(
    bool Enforce,
    SetupCounts CurrentCounts,
    int RemainingSeats,
    int AvailableTownsfolk,
    int AvailableOutsiders,
    int AvailableMinions,
    int AvailableDemons)
{
    /// <summary>
    /// A context that imposes no reachability constraints; every candidate distribution is kept.
    /// Used by callers that do not supply draft state and mirrors the trivially-feasible pre-draft state.
    /// </summary>
    public static readonly SetupFeasibilityContext Unconstrained =
        new(false, SetupCounts.Empty, 0, 0, 0, 0, 0);

    /// <summary>
    /// Determines whether the given target distribution can still be completed by a legal draft,
    /// given the locked-in <see cref="CurrentCounts"/>, the <see cref="RemainingSeats"/>, and the
    /// per-type pool of still-available characters.
    /// <para>
    /// The good side (Townsfolk/Outsiders) is filled strictly per-type. On the evil side a real Demon
    /// can never be fabricated, but a Demon-typed character carrying a Minion-swap rule (e.g. Lil'
    /// Monsta) occupies a Minion distribution slot while still drafting from the Demon pool, so spare
    /// Demons may satisfy a swapped-Minion need.
    /// </para>
    /// <para>
    /// When <see cref="Enforce"/> is <see langword="false"/> this always returns
    /// <see langword="true"/> (no pruning).
    /// </para>
    /// </summary>
    /// <param name="target">The candidate distribution to test for reachability.</param>
    /// <returns><see langword="true"/> if the distribution is reachable; otherwise <see langword="false"/>.</returns>
    public bool IsReachable(SetupCounts target)
    {
        if (!Enforce)
        {
            return true;
        }

        var neededTownsfolk = target.Townsfolk - CurrentCounts.Townsfolk;
        var neededOutsiders = target.Outsiders - CurrentCounts.Outsiders;
        var neededMinions = target.Minions - CurrentCounts.Minions;
        var neededDemons = target.Demons - CurrentCounts.Demons;

        if (neededTownsfolk < 0 || neededOutsiders < 0 || neededMinions < 0 || neededDemons < 0)
        {
            return false;
        }

        var totalNeeded = neededTownsfolk + neededOutsiders + neededMinions + neededDemons;

        // Good side (Townsfolk/Outsiders) is strict per-type: an Outsider seat can only be filled by
        // an Outsider-typed character, etc.
        if (neededTownsfolk > AvailableTownsfolk || neededOutsiders > AvailableOutsiders)
        {
            return false;
        }

        // Evil side: a real Demon can never be fabricated, so the Demon need must be covered by the
        // Demon pool. However, a Demon-typed character carrying a MinionSwap rule (e.g. Lil' Monsta)
        // occupies a Minion *distribution* slot while still being drafted from the Demon pool. Any
        // Demon not consumed by the Demon need is therefore available to satisfy a swapped Minion seat.
        if (neededDemons > AvailableDemons)
        {
            return false;
        }

        var spareDemons = AvailableDemons - neededDemons;
        if (neededMinions > AvailableMinions + spareDemons)
        {
            return false;
        }

        return totalNeeded <= RemainingSeats;
    }
}
