using Curator.Core.Content;

namespace Curator.Core.Rules;

/// <summary>The outcome chosen for a decision, with the category it counts as.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Key">Where it came from, e.g. override:board-clerk-1:common-wards or generic:harmMisuse:1.</param>
/// <param name="Category">The category (an override's own, else the evaluated one).</param>
/// <param name="Cause">The cause, for harm and mixed.</param>
public sealed record OutcomeChoice(Outcome Outcome, string Key, OutcomeCategory Category, Cause Cause);
