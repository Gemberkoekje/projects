namespace Klaverjas.WebApp.Pages.Beheer;

/// <summary>"Zo ziet de e-mail eruit": the mail as the number 1 gets it, from the standings online (the mock's mail view).</summary>
public sealed class MailVoorbeeldModel(MailOphalenHandler ophalen) : PageModel
{
    private readonly MailOphalenHandler ophalen = ophalen ?? throw new ArgumentNullException(nameof(ophalen));

    /// <summary>The mail.</summary>
    public MailVoorbeeld Voorbeeld { get; private set; } = new MailVoorbeeld();

    /// <summary>Loads the mail.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The page, or Stand while nothing is online.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await ophalen.HandleAsync(new MailVoorbeeldOphalen { SiteAdres = $"{Request.Scheme}://{Request.Host}" }, cancellationToken);
        if (!result.IsValid)
        {
            return RedirectToPage("/Beheer/Stand");
        }

        Voorbeeld = result.Value;
        return Page();
    }
}
