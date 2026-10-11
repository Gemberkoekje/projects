namespace Curator.Core.Events;

/// <summary>
/// The spells a patron tries from a book they took home: one drawn among the pages that meet their
/// need, and one they're tempted by if the book holds it (BUILD_BRIEF §5.11).
/// </summary>
/// <param name="PatronId">The patron.</param>
/// <param name="VisitId">The visit the book went out for.</param>
/// <param name="LoanId">The loan.</param>
/// <param name="BookId">The book.</param>
/// <param name="PageIds">The pages tried, the need's first; empty only for a book with no pages left.</param>
public sealed record SpellsTried(string PatronId, string VisitId, string LoanId, string BookId, IReadOnlyList<string> PageIds) : GameEvent;
