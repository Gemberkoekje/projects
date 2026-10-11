namespace Curator.Core.Content;

/// <summary>A day of a named month.</summary>
public sealed record CalendarDate
{
    public required int Day { get; init; }

    public required string Month { get; init; }
}
