using Microsoft.AspNetCore.Http.HttpResults;
using System.Text;

namespace DatumPrikker.WebApp;

/// <summary>
/// <c>/p/{token}/agenda.ics</c>: the picked date as an agenda file, for anyone with the link (D22).
/// Until a date is picked it is a 404, like a poll that doesn't exist.
/// </summary>
public static class AgendaEndpoint
{
    /// <summary>The route.</summary>
    public const string Route = "/p/{token}/agenda.ics";

    /// <summary>Writes the file.</summary>
    /// <param name="token">The code from the link.</param>
    /// <param name="request">The request, for the site's address in the event's description.</param>
    /// <param name="agenda">The query handler.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The file, or 404.</returns>
    public static Task<Results<FileContentHttpResult, NotFound>> HandleAsync(string token, HttpRequest request, GetAgendaHandler agenda, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(agenda);

        return WriteAsync(token, request, agenda, cancellationToken);
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> WriteAsync(string token, HttpRequest request, GetAgendaHandler agenda, CancellationToken cancellationToken)
    {
        var file = await agenda.HandleAsync(new GetAgenda { ShareToken = token, SiteUrl = $"{request.Scheme}://{request.Host}" }, cancellationToken);
        if (!file.IsValid)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.File(Encoding.UTF8.GetBytes(file.Value.Text), AgendaFile.ContentType, Weergave.AgendaBestand(file.Value.Title));
    }
}
