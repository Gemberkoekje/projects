namespace DatumPrikker.application.ReadModels;

/// <summary>Everybody's access, for the admin page (D18).</summary>
public sealed record AccessOverview
{
    /// <summary>Waiting requests, oldest first.</summary>
    public IReadOnlyList<Access> Requested { get; init; } = [];

    /// <summary>Approved accounts, by name.</summary>
    public IReadOnlyList<Access> Approved { get; init; } = [];

    /// <summary>Declined and revoked accounts, latest decision first.</summary>
    public IReadOnlyList<Access> Refused { get; init; } = [];

    /// <summary>Pre-approved addresses, alphabetically.</summary>
    public IReadOnlyList<PreApproval> PreApprovals { get; init; } = [];
}
