namespace Klaverjas.WebApp.Pages;

/// <summary>
/// The players' page (D5): the standings Dad put online, with his note, the evening's winners and
/// the points and pits rankings; the whole month on a computer. Read-only. "Stand printen" opens the
/// PDF of the same standings, the sheet Dad mails (Dad, 2026-11-25).
/// </summary>
public sealed class IndexModel(OnlineStandOphalenHandler ophalen, MailOphalenHandler pdf) : PageModel
{
    private readonly OnlineStandOphalenHandler ophalen = ophalen ?? throw new ArgumentNullException(nameof(ophalen));
    private readonly MailOphalenHandler pdf = pdf ?? throw new ArgumentNullException(nameof(pdf));

    /// <summary>What is online.</summary>
    public OnlineStand Online { get; private set; } = new OnlineStand();

    /// <summary>Loads what is online.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Online = (await ophalen.HandleAsync(new OnlineStandOphalen(), cancellationToken)).Value;
    }

    /// <summary>"Stand printen": the PDF of the standings online, opened in the browser to print.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The PDF, or this page while nothing is online.</returns>
    public async Task<IActionResult> OnGetPdfAsync(CancellationToken cancellationToken)
    {
        var result = await pdf.HandleAsync(new StandPdfOphalen(), cancellationToken);
        if (!result.IsValid)
        {
            return RedirectToPage();
        }

        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(result.Value.Bestandsnaam)}";
        return File(result.Value.Inhoud.ToArray(), "application/pdf");
    }
}
