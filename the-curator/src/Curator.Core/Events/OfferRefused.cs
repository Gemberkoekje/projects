namespace Curator.Core.Events;

/// <summary>The patron turned down an offered book.</summary>
/// <param name="BookId">The book offered.</param>
/// <param name="Line">What they said.</param>
public sealed record OfferRefused(string BookId, string Line) : GameEvent;
