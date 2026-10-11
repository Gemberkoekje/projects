namespace Curator.Core.Events;

/// <summary>The patron's patience fell.</summary>
/// <param name="Amount">Patience spent.</param>
/// <param name="Remaining">Patience left.</param>
public sealed record PatienceSpent(int Amount, int Remaining) : GameEvent;
