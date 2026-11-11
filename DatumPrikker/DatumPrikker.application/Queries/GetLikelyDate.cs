namespace DatumPrikker.application.Queries;

/// <summary>The most likely date of a poll, for somebody who answered it (D21).</summary>
public sealed record GetLikelyDate
{
    /// <summary>The code from the poll's link.</summary>
    public string ShareToken { get; init; } = string.Empty;

    /// <summary>The email address the asker answered with, as typed or remembered.</summary>
    public string Email { get; init; } = string.Empty;
}
