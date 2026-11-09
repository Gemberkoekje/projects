namespace DatumPrikker.core.Models;

/// <summary>What somebody filled in to make a poll; <see cref="Poll.Create"/> checks it.</summary>
public sealed record PollDraft
{
    /// <summary>The title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional details.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Optional place.</summary>
    public string Location { get; init; } = string.Empty;

    /// <summary>The proposed dates; their ids are ignored and replaced.</summary>
    public IReadOnlyList<DateOption> Options { get; init; } = [];
}
