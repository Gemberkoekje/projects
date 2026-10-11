namespace Curator.Core.Projections;

/// <summary>A loan on a library card.</summary>
/// <param name="BookTitle">The book.</param>
/// <param name="BorrowedDate">When borrowed.</param>
/// <param name="ReturnedDate">When returned, or empty while out.</param>
public sealed record CardLoanRow(
    string BookTitle,
    string BorrowedDate,
    string ReturnedDate);
