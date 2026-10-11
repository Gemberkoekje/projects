using Curator.Core.Game;

namespace Curator.Core.Projections;

/// <summary>Hidden state for the F1 overlay (BUILD_BRIEF §7.10).</summary>
/// <param name="Seed">The seed.</param>
/// <param name="Day">The day.</param>
/// <param name="Phase">The phase.</param>
/// <param name="Reputation">Hidden reputation.</param>
/// <param name="Band">Its band.</param>
/// <param name="VisitId">The visit at the counter, or empty.</param>
/// <param name="Goal">The visit's goal.</param>
/// <param name="Temptations">The visit's temptations.</param>
/// <param name="Trust">The patron's trust.</param>
/// <param name="Patience">Their patience.</param>
/// <param name="Outcomes">Resolved outcomes.</param>
/// <param name="Flags">Flags set.</param>
/// <param name="RecentEvents">The last 20 events.</param>
public sealed record DebugView(
    long Seed,
    int Day,
    DayPhase Phase,
    int Reputation,
    ReputationBand Band,
    string VisitId,
    string Goal,
    string Temptations,
    int Trust,
    int Patience,
    IReadOnlyList<PendingOutcomeView> Outcomes,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> RecentEvents);
