namespace Curator.Core.Events;

/// <summary>A debug cheat was used; logged so replays and reports show it.</summary>
/// <param name="Cheat">Which cheat.</param>
/// <param name="Detail">Its argument.</param>
public sealed record DebugCheatUsed(string Cheat, string Detail) : GameEvent;
