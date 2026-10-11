namespace Curator.Core.Events;

/// <summary>The curator wrote a loan's return date into the notebook.</summary>
/// <param name="LoanId">The loan.</param>
public sealed record LedgerReturnNoted(string LoanId) : GameEvent;
