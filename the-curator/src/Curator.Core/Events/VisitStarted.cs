namespace Curator.Core.Events;

/// <summary>A patron came to the counter.</summary>
/// <param name="VisitId">The visit.</param>
/// <param name="PatronId">The patron.</param>
/// <param name="Step">The visit's step in the patron's arc.</param>
/// <param name="SlotIndex">Today's slot.</param>
/// <param name="SlotId">The slot as scheduled: a patron id or filler.</param>
/// <param name="Trust">The patron's trust as the visit starts.</param>
/// <param name="Patience">Starting patience.</param>
/// <param name="Forced">Whether only lending is allowed.</param>
/// <param name="RequestedBookId">The book asked for, or empty.</param>
/// <param name="Topic">The spine tag of what they asked for.</param>
/// <param name="FirstVisit">Whether this is the first time the curator meets them.</param>
public sealed record VisitStarted(string VisitId, string PatronId, int Step, int SlotIndex, string SlotId, int Trust, int Patience, bool Forced, string RequestedBookId, string Topic, bool FirstVisit) : GameEvent;
