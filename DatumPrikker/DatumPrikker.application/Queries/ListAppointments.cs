namespace DatumPrikker.application.Queries;

/// <summary>"Mijn afspraken": the polls an account made and the polls its email answered (D6).</summary>
public sealed record ListAppointments
{
    /// <summary>Whose afspraken.</summary>
    public Account Account { get; init; } = Account.Anonymous;
}
