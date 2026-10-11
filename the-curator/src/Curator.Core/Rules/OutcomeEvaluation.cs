using Curator.Core.Content;

namespace Curator.Core.Rules;

/// <summary>The rule-based category and cause for lending a book to a visit.</summary>
/// <param name="Category">Good, unhelpful, harm or mixed.</param>
/// <param name="Cause">Misuse or accident for harm and mixed, else None.</param>
public sealed record OutcomeEvaluation(OutcomeCategory Category, Cause Cause);
