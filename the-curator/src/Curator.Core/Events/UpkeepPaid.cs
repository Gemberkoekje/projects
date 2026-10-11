namespace Curator.Core.Events;

/// <summary>The evening's upkeep was paid.</summary>
/// <param name="Amount">The upkeep.</param>
public sealed record UpkeepPaid(int Amount) : GameEvent;
