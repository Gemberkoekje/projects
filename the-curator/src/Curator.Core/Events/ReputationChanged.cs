namespace Curator.Core.Events;

/// <summary>The library's hidden reputation changed.</summary>
/// <param name="Delta">The change.</param>
/// <param name="Reputation">Reputation now.</param>
/// <param name="Reason">Why.</param>
public sealed record ReputationChanged(int Delta, int Reputation, string Reason) : GameEvent;
