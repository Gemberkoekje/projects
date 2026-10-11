namespace Curator.Core.Events;

/// <summary>Mana was spent on a spell.</summary>
/// <param name="Amount">Mana spent.</param>
/// <param name="Remaining">Mana left.</param>
/// <param name="Purpose">readThoughts or identify.</param>
public sealed record ManaSpent(int Amount, int Remaining, string Purpose) : GameEvent;
