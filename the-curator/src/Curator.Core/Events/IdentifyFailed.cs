namespace Curator.Core.Events;

/// <summary>The book resisted Identify outright.</summary>
/// <param name="BookId">The book.</param>
public sealed record IdentifyFailed(string BookId) : GameEvent;
