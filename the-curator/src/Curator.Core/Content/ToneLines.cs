namespace Curator.Core.Content;

/// <summary>Lines about the library, by reputation band.</summary>
public sealed record ToneLines
{
    public required IReadOnlyList<string> Wary { get; init; }

    public required IReadOnlyList<string> Neutral { get; init; }

    public required IReadOnlyList<string> Warm { get; init; }
}
