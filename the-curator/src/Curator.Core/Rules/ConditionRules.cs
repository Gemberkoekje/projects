using Curator.Core.Content;
using Curator.Core.Game;

namespace Curator.Core.Rules;

/// <summary>Evaluates <c>when</c> conditions: every field present must hold (BUILD_BRIEF §5.12).</summary>
public static class ConditionRules
{
    /// <summary>Whether a condition holds.</summary>
    /// <param name="when">The condition.</param>
    /// <param name="facts">The patron's facts.</param>
    /// <returns>True when every field present holds.</returns>
    public static bool Holds(Condition when, ConditionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(facts);
        if (when.MinTrust is { } min && facts.Trust < min)
        {
            return false;
        }

        if (when.MaxTrust is { } max && facts.Trust > max)
        {
            return false;
        }

        if (!when.FlagsAll.All(facts.Flags.Contains) || when.FlagsNone.Any(facts.Flags.Contains))
        {
            return false;
        }

        if (when.PreviousDecisionIn.Count > 0 && !when.PreviousDecisionIn.Contains(facts.LastDecision))
        {
            return false;
        }

        if (when.PreviousOutcomeIn.Count > 0 && !when.PreviousOutcomeIn.Contains(facts.LastLendCategory))
        {
            return false;
        }

        return when.HasBorrowed.Length == 0 || facts.Borrowed.Contains(when.HasBorrowed);
    }

    /// <summary>Gathers a patron's facts from the state.</summary>
    /// <param name="content">The content.</param>
    /// <param name="state">The state.</param>
    /// <param name="patronId">The patron.</param>
    /// <returns>The facts.</returns>
    public static ConditionFacts FactsFor(ContentSet content, GameState state, string patronId)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        var met = state.HasMet(patronId);
        return new ConditionFacts(
            TrustRules.Current(content, state, patronId),
            state.Flags,
            met ? state.Patron(patronId).LastDecision : DecisionKind.None,
            met ? state.Patron(patronId).LastLendCategory : OutcomeCategory.None,
            BorrowedBooks(content, state, patronId));
    }

    /// <summary>Every book a patron has borrowed: pre-game card history and in-game loans.</summary>
    /// <param name="content">The content.</param>
    /// <param name="state">The state.</param>
    /// <param name="patronId">The patron.</param>
    /// <returns>The book ids.</returns>
    public static IReadOnlySet<string> BorrowedBooks(ContentSet content, GameState state, string patronId)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        var borrowed = content.Patron(patronId).PreGameLoans.Select(l => l.BookId).ToHashSet(StringComparer.Ordinal);
        if (state.HasMet(patronId))
        {
            borrowed.UnionWith(state.Patron(patronId).LoanIds.Select(id => state.Loan(id).BookId));
        }

        return borrowed;
    }
}
