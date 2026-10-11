using Curator.Core.Content;

namespace Curator.Core.Rules;

/// <summary>What a <c>when</c> condition is checked against, for one patron.</summary>
/// <param name="Trust">The patron's current trust.</param>
/// <param name="Flags">Every flag set so far.</param>
/// <param name="LastDecision">How their last visit ended, or None.</param>
/// <param name="LastLendCategory">The true outcome of their last lend, or None.</param>
/// <param name="Borrowed">Every book they have borrowed, card history included.</param>
public sealed record ConditionFacts(
    int Trust,
    IReadOnlySet<string> Flags,
    DecisionKind LastDecision,
    OutcomeCategory LastLendCategory,
    IReadOnlySet<string> Borrowed);
