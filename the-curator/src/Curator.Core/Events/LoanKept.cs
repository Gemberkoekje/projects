namespace Curator.Core.Events;

/// <summary>
/// A book due back stays with its borrower because it already holds what their next visit would
/// have asked for (BUILD_BRIEF §5.11): that visit is skipped and counts as lent this book.
/// </summary>
/// <param name="LoanId">The loan.</param>
/// <param name="BookId">The book.</param>
/// <param name="PatronId">The patron.</param>
/// <param name="VisitId">The visit skipped.</param>
/// <param name="Step">The visit's step, now used.</param>
/// <param name="DueDay">The new due day.</param>
/// <param name="Returns">False when the book now never comes back.</param>
public sealed record LoanKept(string LoanId, string BookId, string PatronId, string VisitId, int Step, int DueDay, bool Returns) : GameEvent;
