namespace Curator.Core.Content;

/// <summary>The in-world calendar used to print dates.</summary>
public sealed record Calendar
{
    public required IReadOnlyList<string> Months { get; init; }

    public required int DaysPerMonth { get; init; }

    /// <summary>The date of day 1.</summary>
    public required CalendarDate Day1 { get; init; }
}
