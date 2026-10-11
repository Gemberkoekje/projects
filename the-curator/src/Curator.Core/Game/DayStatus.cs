namespace Curator.Core.Game;

/// <summary>Where the game is in its day cycle.</summary>
public enum DayStatus
{
    None = 0,

    /// <summary>The library is open: the morning desk, visits, and the time between them.</summary>
    Open,

    /// <summary>The evening has passed; the next morning follows at once.</summary>
    Ended,

    /// <summary>Day 7's evening has passed.</summary>
    WeekOver,
}
