namespace DatumPrikker.core.Models;

/// <summary>One date a poll proposes: a whole day, or a time range on that day.</summary>
public sealed record DateOption
{
    /// <summary>Identifies the option within its poll.</summary>
    public Guid Id { get; init; }

    /// <summary>The day.</summary>
    public DateOnly Date { get; init; }

    /// <summary>False for the whole day; then <see cref="Start"/> and <see cref="End"/> mean nothing.</summary>
    public bool HasTime { get; init; }

    /// <summary>When it starts, local time.</summary>
    public TimeOnly Start { get; init; }

    /// <summary>When it ends, local time; always after <see cref="Start"/>.</summary>
    public TimeOnly End { get; init; }

    /// <summary>True when both describe the same day and times, whatever their ids.</summary>
    /// <param name="other">The option to compare with.</param>
    /// <returns>True for the same slot.</returns>
    public bool SameSlotAs(DateOption other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Date == other.Date
            && HasTime == other.HasTime
            && (!HasTime || (Start == other.Start && End == other.End));
    }
}
