namespace DatumPrikker.WebApp.Pages;

/// <summary>The landing page; signed in, it lists your afspraken.</summary>
[AllowAnonymous]
public sealed class IndexModel(ListAppointmentsHandler handler) : PageModel
{
    private readonly ListAppointmentsHandler handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <summary>Who is looking.</summary>
    public Account Account { get; private set; } = Account.Anonymous;

    /// <summary>Their afspraken; empty when anonymous.</summary>
    public IReadOnlyList<Appointment> Appointments { get; private set; } = [];

    /// <summary>Shows the page.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Account = AccountClaims.From(User);
        Appointments = await handler.HandleAsync(new ListAppointments { Account = Account }, cancellationToken);
    }
}
