namespace DatumPrikker.WebApp.Pages;

/// <summary>"Mijn afspraken": the polls you made and answered (signed in only, via the fallback policy).</summary>
public sealed class AfsprakenModel(ListAppointmentsHandler handler) : PageModel
{
    private readonly ListAppointmentsHandler handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <summary>The afspraken.</summary>
    public IReadOnlyList<Appointment> Appointments { get; private set; } = [];

    /// <summary>The address answers are matched on.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Loads the list.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var account = AccountClaims.From(User);
        Email = account.Email;
        Appointments = await handler.HandleAsync(new ListAppointments { Account = account }, cancellationToken);
    }
}
