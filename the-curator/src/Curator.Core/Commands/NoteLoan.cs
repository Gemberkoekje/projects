namespace Curator.Core.Commands;

/// <summary>Write the loan just made into the notebook's watched loans.</summary>
/// <param name="LoanId">The loan.</param>
public sealed record NoteLoan(string LoanId) : Command;
