namespace DatumPrikker.WebApp;

/// <summary>The rate limit on answering (D11).</summary>
public static class RateLimits
{
    /// <summary>The policy name; it only counts POSTs.</summary>
    public const string Answers = "antwoorden";

    /// <summary>How many answers one address may send per <see cref="Window"/>.</summary>
    public const int AnswersPerWindow = 30;

    /// <summary>The fixed window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
}
