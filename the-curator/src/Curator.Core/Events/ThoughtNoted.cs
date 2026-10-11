namespace Curator.Core.Events;

/// <summary>The curator copied a Read Thoughts fragment into the notebook.</summary>
/// <param name="PatronId">The patron.</param>
/// <param name="VisitId">The visit.</param>
/// <param name="Fragment">The fragment.</param>
public sealed record ThoughtNoted(string PatronId, string VisitId, string Fragment) : GameEvent;
