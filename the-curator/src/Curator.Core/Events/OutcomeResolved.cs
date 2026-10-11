using Curator.Core.Content;

namespace Curator.Core.Events;

/// <summary>What came of a visit, settled now and revealed later (BUILD_BRIEF §5.11).</summary>
/// <param name="ResolutionId">The new resolution.</param>
/// <param name="PatronId">The patron.</param>
/// <param name="VisitId">The visit.</param>
/// <param name="BookId">The book lent, or empty.</param>
/// <param name="LoanId">The loan, or empty.</param>
/// <param name="Category">The category.</param>
/// <param name="Cause">The cause, for harm and mixed.</param>
/// <param name="OutcomeKey">Which outcome was used, e.g. visit:wren-hale-1:mixedAccident.</param>
/// <param name="Channel">How it will surface.</param>
/// <param name="SurfaceDay">When it will surface.</param>
/// <param name="Headline">Newspaper headline, or empty.</param>
/// <param name="From">Letter sender, or empty.</param>
/// <param name="Text">The text, with {name} and {book} filled in.</param>
/// <param name="TrustDelta">Trust change when it surfaces.</param>
/// <param name="HasReputation">Whether the outcome sets its own reputation change.</param>
/// <param name="Reputation">Its own reputation change, when it has one.</param>
/// <param name="RemovePageOnReturn">A page torn out when the book returns, or empty.</param>
public sealed record OutcomeResolved(string ResolutionId, string PatronId, string VisitId, string BookId, string LoanId, OutcomeCategory Category, Cause Cause, string OutcomeKey, OutcomeChannel Channel, int SurfaceDay, string Headline, string From, string Text, int TrustDelta, bool HasReputation, int Reputation, string RemovePageOnReturn) : GameEvent;
