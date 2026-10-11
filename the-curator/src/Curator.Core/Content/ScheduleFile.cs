namespace Curator.Core.Content;

/// <summary>game/content/schedule.json: the calendar and each day's slots.</summary>
public sealed record ScheduleFile
{
    public required Calendar Calendar { get; init; }

    public required IReadOnlyList<ScheduleDay> Days { get; init; }

    public required bool Placeholder { get; init; }
}
