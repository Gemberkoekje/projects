namespace Curator.Core.Game;

/// <summary>The time of day; each finished visit moves it on, and the evening is night.</summary>
public enum DayPhase
{
    None = 0,
    Morning,
    Midday,
    Afternoon,
    Dusk,
    Night,
}
