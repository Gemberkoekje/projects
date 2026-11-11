namespace DatumPrikker.core.Models;

/// <summary>What somebody filled in to make a poll; <see cref="Poll.Create"/> checks it.</summary>
public sealed record PollDraft
{
    /// <summary>The title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The maker's name as respondents will see it; empty means the account's name.</summary>
    public string OwnerName { get; init; } = string.Empty;

    /// <summary>Optional details.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Optional place.</summary>
    public string Location { get; init; } = string.Empty;

    /// <summary>The proposed dates; their ids are ignored and replaced.</summary>
    public IReadOnlyList<DateOption> Options { get; init; } = [];

    /// <summary>The local day the poll closes by itself; <see langword="default"/> for no deadline (D22).</summary>
    public DateOnly DeadlineDate { get; init; }

    /// <summary>False when the maker gave only a day: then it closes at the end of that day.</summary>
    public bool DeadlineHasTime { get; init; }

    /// <summary>The local time it closes; means nothing without <see cref="DeadlineHasTime"/>.</summary>
    public TimeOnly DeadlineTime { get; init; }
}
