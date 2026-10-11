using Curator.Core.Events;

namespace Curator.Core.Game;

/// <summary>A saved game: the event log, plus the curator's free text (BUILD_BRIEF §7.12).</summary>
public sealed record SaveData
{
    /// <summary>The schema this build writes.</summary>
    public const int CurrentSchemaVersion = 1;

    public required int SchemaVersion { get; init; }

    public required long Seed { get; init; }

    /// <summary>The content hash the game was played with.</summary>
    public required string ContentVersion { get; init; }

    public required IReadOnlyList<GameEvent> Events { get; init; }

    public IReadOnlyDictionary<string, string> FreeNotes { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> LedgerNotes { get; init; } = new Dictionary<string, string>();
}
