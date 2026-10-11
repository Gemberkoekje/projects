namespace Curator.Core.Events;

/// <summary>The curator wrote a loan into the notebook's watched loans.</summary>
/// <param name="LoanId">The loan.</param>
public sealed record LedgerEntryNoted(string LoanId) : GameEvent;
