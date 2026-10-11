namespace Curator.Core.Events;

/// <summary>The patron ran out of patience and trust, and left.</summary>
/// <param name="VisitId">The visit.</param>
public sealed record PatronWalkedOut(string VisitId) : GameEvent;
