namespace Curator.Core.Events;

/// <summary>Mana was added outside the morning grant (debug).</summary>
/// <param name="Amount">Mana added.</param>
/// <param name="Total">Mana now.</param>
public sealed record ManaAdded(int Amount, int Total) : GameEvent;
