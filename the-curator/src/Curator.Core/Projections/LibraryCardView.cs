namespace Curator.Core.Projections;

/// <summary>The patron's library card: true and factual (GDD §9).</summary>
/// <param name="PatronId">The patron.</param>
/// <param name="Name">Their name.</param>
/// <param name="Description">What the card notes about them.</param>
/// <param name="FirstVisitDate">Their first visit.</param>
/// <param name="Loans">Every loan, oldest first.</param>
public sealed record LibraryCardView(
    string PatronId,
    string Name,
    string Description,
    string FirstVisitDate,
    IReadOnlyList<CardLoanRow> Loans);
