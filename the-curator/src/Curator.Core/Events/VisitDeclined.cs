namespace Curator.Core.Events;

/// <summary>The curator handed back the card.</summary>
/// <param name="VisitId">The visit.</param>
public sealed record VisitDeclined(string VisitId) : GameEvent;
