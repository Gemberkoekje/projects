namespace Klaverjas.WebApp.Pages;

/// <summary>
/// The players' page (D5): the standings Dad put online, with his note, the evening's winners and
/// the points and pits rankings; the whole month on a computer. Read-only.
/// </summary>
public sealed class IndexModel(OnlineStandOphalenHandler ophalen) : PageModel
{
    private readonly OnlineStandOphalenHandler ophalen = ophalen ?? throw new ArgumentNullException(nameof(ophalen));

    /// <summary>What is online.</summary>
    public OnlineStand Online { get; private set; } = new OnlineStand();

    /// <summary>Loads what is online.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Online = (await ophalen.HandleAsync(new OnlineStandOphalen(), cancellationToken)).Value;
    }
}
