namespace DatumPrikker.application.Queries;

/// <summary>How many access requests wait, for the admin's header badge (D17).</summary>
public sealed record CountPendingRequests
{
    /// <summary>Who is looking; anybody but an admin gets 0.</summary>
    public Account By { get; init; } = Account.Anonymous;
}
