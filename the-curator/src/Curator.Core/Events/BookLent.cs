namespace Curator.Core.Events;

/// <summary>A book went out on loan.</summary>
/// <param name="LoanId">The new loan.</param>
/// <param name="BookId">The book.</param>
/// <param name="PatronId">The borrower.</param>
/// <param name="VisitId">The visit.</param>
/// <param name="Fee">The fee earned.</param>
/// <param name="AsAlternative">Whether it was taken instead of the book asked for.</param>
/// <param name="DueDay">The morning it comes back.</param>
/// <param name="Returns">False when it never comes back.</param>
public sealed record BookLent(string LoanId, string BookId, string PatronId, string VisitId, int Fee, bool AsAlternative, int DueDay, bool Returns) : GameEvent;
