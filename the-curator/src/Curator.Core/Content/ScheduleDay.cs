namespace Curator.Core.Content;

/// <summary>One day of the schedule: its slots in order, and optionally a fixed mana grant.</summary>
public sealed record ScheduleDay
{
    public required int Day { get; init; }

    /// <summary>When set, the morning grant is exactly this, ignoring bonus and rollover (day 1).</summary>
    public int? StartMana { get; init; }

    /// <summary>Patron ids or "filler".</summary>
    public required IReadOnlyList<string> Slots { get; init; }
}
