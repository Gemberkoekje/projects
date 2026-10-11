namespace Curator.Core.Projections;

/// <summary>A watched loan in the notebook.</summary>
/// <param name="LoanId">The loan.</param>
/// <param name="BookTitle">The book.</param>
/// <param name="BorrowedDate">When borrowed.</param>
/// <param name="ReturnedDate">When its return was noted, or empty.</param>
/// <param name="Note">The curator's note.</param>
public sealed record LedgerRow(
    string LoanId,
    string BookTitle,
    string BorrowedDate,
    string ReturnedDate,
    string Note);
