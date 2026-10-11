namespace Curator.Core.Events;

/// <summary>A lent book came back this morning.</summary>
/// <param name="LoanId">The loan.</param>
/// <param name="BookId">The book.</param>
/// <param name="PatronId">Who returned it.</param>
public sealed record BookReturned(string LoanId, string BookId, string PatronId) : GameEvent;
