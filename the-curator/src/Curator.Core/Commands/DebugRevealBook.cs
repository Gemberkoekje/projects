namespace Curator.Core.Commands;

/// <summary>Debug builds only: bring every page of a book into focus.</summary>
/// <param name="BookId">The book.</param>
public sealed record DebugRevealBook(string BookId) : Command;
