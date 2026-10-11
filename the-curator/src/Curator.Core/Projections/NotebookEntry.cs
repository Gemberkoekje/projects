namespace Curator.Core.Projections;

/// <summary>A page in the notebook's index.</summary>
/// <param name="PatronId">The patron.</param>
/// <param name="Name">Their name.</param>
public sealed record NotebookEntry(
    string PatronId,
    string Name);
