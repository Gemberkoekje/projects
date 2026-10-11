namespace Curator.Core.Content;

/// <summary>What Read Thoughts finds in a visit.</summary>
public sealed record ReadThoughtsSpec
{
    /// <summary>The fragment when no trust-keyed entry applies.</summary>
    public required string Default { get; init; }

    /// <summary>Fragments by trust level; the nearest entry at or below the current trust wins.</summary>
    public IReadOnlyDictionary<int, string> ByTrust { get; init; } = new Dictionary<int, string>();

    /// <summary>Specific question ids this unlocks.</summary>
    public IReadOnlyList<string> Unlocks { get; init; } = [];
}
