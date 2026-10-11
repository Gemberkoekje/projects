namespace Curator.Core.Rules;

/// <summary>Who fills a slot.</summary>
/// <param name="SlotId">The slot as scheduled: a patron id or "filler".</param>
/// <param name="VisitId">The visit that fills it, or empty when nobody is left.</param>
/// <param name="SlotPatronId">The patron the slot was scheduled for, or empty for filler slots.</param>
/// <param name="SlotPatronCame">Whether that patron is the one who came.</param>
public sealed record SlotResolution(string SlotId, string VisitId, string SlotPatronId, bool SlotPatronCame);
