using Curator.Core.Game;

namespace Curator.Core.Rules;

/// <summary>Time of day: each finished visit moves it on, up to dusk (BUILD_BRIEF §5.1).</summary>
public static class PhaseRules
{
    /// <summary>The phase after a visit ends.</summary>
    /// <param name="phase">The current phase.</param>
    /// <returns>The next phase; dusk stays dusk.</returns>
    public static DayPhase AfterVisit(DayPhase phase) => phase switch
    {
        DayPhase.Morning => DayPhase.Midday,
        DayPhase.Midday => DayPhase.Afternoon,
        _ => DayPhase.Dusk,
    };
}
