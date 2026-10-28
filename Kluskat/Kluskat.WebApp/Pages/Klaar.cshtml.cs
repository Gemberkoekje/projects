namespace Kluskat.WebApp.Pages;

/// <summary>"Klaar voor nu": permission to stop.</summary>
public sealed class KlaarModel(ProfielOphalenHandler profiel) : PageModel
{
    /// <summary>The cat's look.</summary>
    public Profieloverzicht Profiel { get; private set; } = new Profieloverzicht();

    /// <summary>Shows the sleeping cat.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await profiel.HandleAsync(new ProfielOphalen(), cancellationToken);

        if (result.IsValid)
        {
            Profiel = result.Value;
        }
    }
}
