using Curator.Core.Game;

namespace Curator.Core.Events;

/// <summary>The time of day moved on.</summary>
/// <param name="Phase">The new phase.</param>
public sealed record PhaseAdvanced(DayPhase Phase) : GameEvent;
