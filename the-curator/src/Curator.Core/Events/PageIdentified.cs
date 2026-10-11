namespace Curator.Core.Events;

/// <summary>Identify brought a page into focus.</summary>
/// <param name="BookId">The book.</param>
/// <param name="PageId">The page.</param>
/// <param name="Repeat">Whether the page was already known.</param>
public sealed record PageIdentified(string BookId, string PageId, bool Repeat) : GameEvent;
