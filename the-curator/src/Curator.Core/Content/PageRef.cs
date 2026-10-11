namespace Curator.Core.Content;

/// <summary>A page together with the book it belongs to.</summary>
/// <param name="Book">The book.</param>
/// <param name="Page">The page.</param>
public sealed record PageRef(Book Book, Page Page);
