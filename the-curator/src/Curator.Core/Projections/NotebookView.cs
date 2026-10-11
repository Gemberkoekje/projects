namespace Curator.Core.Projections;

/// <summary>The curator's notebook page for a patron (GDD §9).</summary>
/// <param name="PatronId">The patron.</param>
/// <param name="Name">Their name.</param>
/// <param name="FirstVisitDate">Their first visit.</param>
/// <param name="VisitCount">Visits so far.</param>
/// <param name="Trust">The curator's read of their trust, 0-3.</param>
/// <param name="Ledger">Watched loans.</param>
/// <param name="Thoughts">Fragments copied in.</param>
/// <param name="FreeNotes">The curator's own notes.</param>
public sealed record NotebookView(
    string PatronId,
    string Name,
    string FirstVisitDate,
    int VisitCount,
    int Trust,
    IReadOnlyList<LedgerRow> Ledger,
    IReadOnlyList<NotedThoughtView> Thoughts,
    string FreeNotes);
