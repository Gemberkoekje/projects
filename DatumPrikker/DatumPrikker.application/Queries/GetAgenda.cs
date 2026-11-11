namespace DatumPrikker.application.Queries;

/// <summary>The picked date of a poll as an agenda file, for anyone with the link (D22).</summary>
public sealed record GetAgenda
{
    /// <summary>The code from the poll's link.</summary>
    public string ShareToken { get; init; } = string.Empty;

    /// <summary>The site's address, such as <c>https://datumprikker.gemberkoekje.nl</c>, for the poll's link in the event.</summary>
    public string SiteUrl { get; init; } = string.Empty;
}
