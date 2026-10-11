namespace Curator.Core.Commands;

/// <summary>Cast Identify on a book in the library (1 mana).</summary>
/// <param name="BookId">The book.</param>
public sealed record Identify(string BookId) : Command;
