using Kluskat.application.Spel;

namespace Kluskat.WebApp.Pages;

/// <summary>
/// The cat's corner: level, streak, the wardrobe and the trophy shelf. Every number here counts what
/// was done; the page has nothing to say about what is left.
/// </summary>
public sealed class ProfielModel(
    ProfielOphalenHandler ophalen,
    AccessoireKiezenHandler kiezen,
    ILogger<ProfielModel> logger) : PageModel
{
    /// <summary>The score and the look.</summary>
    public Profieloverzicht Profiel { get; private set; } = new Profieloverzicht();

    /// <summary>A one-off message after a failed choice, carried over the redirect.</summary>
    [TempData]
    public string Melding { get; set; } = string.Empty;

    /// <summary>Every accessory with the level that unlocks it, plus "nothing" first.</summary>
    public IReadOnlyList<(int Level, Accessoire Accessoire)> Kast { get; } = [(1, Accessoire.None), .. Spelregels.Garderobe];

    /// <summary>Every achievement there is, earned or not.</summary>
    public IReadOnlyList<Trofee> AlleTrofeeen { get; } = Trofeeen.Alle;

    /// <summary>Shows the profile.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await ophalen.HandleAsync(new ProfielOphalen(), cancellationToken);

        if (result.IsValid)
        {
            Profiel = result.Value;
        }
    }

    /// <summary>Dresses the cat.</summary>
    /// <param name="accessoire">What to wear.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>This page again.</returns>
    public async Task<IActionResult> OnPostAantrekkenAsync(Accessoire accessoire, CancellationToken cancellationToken)
    {
        var result = await kiezen.HandleAsync(new AccessoireKiezen { Accessoire = accessoire }, cancellationToken);

        if (!result.IsValid)
        {
            Melding = string.Join(" ", result.Messages.Select(m => m.Message));
            logger.LogWarning("Aantrekken van {Accessoire} afgewezen: {Reden}", accessoire, Melding);
        }

        return RedirectToPage();
    }

    /// <summary>Whether an achievement has been earned.</summary>
    /// <param name="trofee">The achievement.</param>
    /// <returns><see langword="true"/> when it is on the shelf.</returns>
    public bool Behaald(Trofee trofee) => Profiel.Spelstand.Trofeeen.Any(t => t.Id == trofee.Id);
}
