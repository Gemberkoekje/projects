namespace Curator.Core.Content;

/// <summary>A patron in game/content/patrons/&lt;id&gt;.json, with all their visits.</summary>
public sealed record Patron
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required PatronRole Role { get; init; }

    /// <summary>Starting trust 0-3; the balance's stranger start when absent.</summary>
    public int? StartTrust { get; init; }

    /// <summary>A warded mind notices Read Thoughts.</summary>
    public bool Warded { get; init; }

    /// <summary>Fillers only: the order in which one-off visitors are used.</summary>
    public int FillerOrder { get; init; }

    /// <summary>Card history from before the game.</summary>
    public IReadOnlyList<PreGameLoan> PreGameLoans { get; init; } = [];

    public required IReadOnlyList<Visit> Visits { get; init; }

    public required bool Placeholder { get; init; }
}
