namespace Curator.Core.Events;

/// <summary>Tomorrow's attentiveness bonus was fixed: today's visits investigated before the decision.</summary>
/// <param name="InvestigatedVisits">How many.</param>
public sealed record AttentivenessRecorded(int InvestigatedVisits) : GameEvent;
