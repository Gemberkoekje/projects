namespace Curator.Core.Content;

/// <summary>A visit together with its patron.</summary>
/// <param name="Patron">The patron.</param>
/// <param name="Visit">The visit.</param>
public sealed record VisitRef(Patron Patron, Visit Visit);
