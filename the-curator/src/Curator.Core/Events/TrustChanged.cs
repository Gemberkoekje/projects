namespace Curator.Core.Events;

/// <summary>A patron's trust changed.</summary>
/// <param name="PatronId">The patron.</param>
/// <param name="Delta">The change asked for.</param>
/// <param name="Trust">Trust after clamping to 0-3.</param>
/// <param name="Reason">Why.</param>
public sealed record TrustChanged(string PatronId, int Delta, int Trust, string Reason) : GameEvent;
