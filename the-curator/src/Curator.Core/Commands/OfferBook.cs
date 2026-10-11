namespace Curator.Core.Commands;

/// <summary>Offer the patron another book instead.</summary>
/// <param name="BookId">The book offered.</param>
public sealed record OfferBook(string BookId) : Command;
