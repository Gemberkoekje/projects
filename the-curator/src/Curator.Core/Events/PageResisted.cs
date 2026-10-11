namespace Curator.Core.Events;

/// <summary>Identify landed on a page that won't come into focus.</summary>
/// <param name="BookId">The book.</param>
/// <param name="PageId">The page.</param>
public sealed record PageResisted(string BookId, string PageId) : GameEvent;
