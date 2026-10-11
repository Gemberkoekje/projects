namespace Curator.Core.Content;

/// <summary>game/content/newspaper.json: the masthead, each day's flavour items and tone lines.</summary>
public sealed record NewspaperFile
{
    public required string Masthead { get; init; }

    /// <summary>Ordinary news by day.</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<string>> Flavour { get; init; }

    public required ToneLines ToneLines { get; init; }

    public required bool Placeholder { get; init; }
}
