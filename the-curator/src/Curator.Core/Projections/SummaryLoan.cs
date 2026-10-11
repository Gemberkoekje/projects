namespace Curator.Core.Projections;

/// <summary>A loan in a summary.</summary>
/// <param name="BookTitle">The book.</param>
/// <param name="PatronName">The borrower.</param>
/// <param name="Fee">The fee.</param>
/// <param name="AsAlternative">Taken instead of the book asked for.</param>
public sealed record SummaryLoan(
    string BookTitle,
    string PatronName,
    int Fee,
    bool AsAlternative);
