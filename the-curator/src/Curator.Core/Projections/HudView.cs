using Curator.Core.Game;

namespace Curator.Core.Projections;

/// <summary>What the HUD shows (BUILD_BRIEF §7.7).</summary>
/// <param name="Mana">Mana left today.</param>
/// <param name="Money">Money; may be negative.</param>
/// <param name="Day">The game day.</param>
/// <param name="WeekDays">Days in the week.</param>
/// <param name="Date">The in-world date.</param>
/// <param name="Phase">The time of day, for lighting.</param>
/// <param name="InVisit">Whether a patron is waiting on a decision.</param>
/// <param name="PatronName">The waiting patron, or empty.</param>
/// <param name="Patience">Their patience.</param>
/// <param name="RequestedBookTitle">The book they asked for, or empty.</param>
/// <param name="Topic">The spine tag of what they asked for.</param>
/// <param name="Impatient">Whether their patience is 1 or less.</param>
/// <param name="ImpatientTell">The tell to show when impatient.</param>
/// <param name="SlotsLeft">Whether any of today's slots are still to come.</param>
/// <param name="CanRingBell">Whether the bell can be rung now.</param>
/// <param name="CanClose">Whether the library can close for the day now.</param>
/// <param name="WeekOver">Whether the week has ended.</param>
public sealed record HudView(
    int Mana,
    int Money,
    int Day,
    int WeekDays,
    string Date,
    DayPhase Phase,
    bool InVisit,
    string PatronName,
    int Patience,
    string RequestedBookTitle,
    string Topic,
    bool Impatient,
    string ImpatientTell,
    bool SlotsLeft,
    bool CanRingBell,
    bool CanClose,
    bool WeekOver);
