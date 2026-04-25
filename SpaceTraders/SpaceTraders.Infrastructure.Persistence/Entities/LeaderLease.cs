namespace SpaceTraders.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents the exclusive leader lease used for single-active-instance coordination.
/// The table holds one row per lease key (default: <c>"game-loop"</c>).
/// </summary>
public sealed class LeaderLease
{
    /// <summary>Logical name of the lease (e.g. <c>"game-loop"</c>).</summary>
    public required string Key { get; set; }

    /// <summary>Opaque identifier of the holder (e.g. a GUID generated at startup).</summary>
    public required string HolderId { get; set; }

    /// <summary>UTC timestamp after which the lease may be taken over by another instance.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
