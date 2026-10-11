namespace Curator.Core.Events;

/// <summary>A page was torn out of a book (an outcome's onReturn).</summary>
/// <param name="BookId">The book.</param>
/// <param name="PageId">The page.</param>
public sealed record PageRemoved(string BookId, string PageId) : GameEvent;
