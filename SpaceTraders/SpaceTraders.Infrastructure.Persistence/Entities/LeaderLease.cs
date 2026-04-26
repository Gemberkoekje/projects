namespace SpaceTraders.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents the exclusive leader lease used for single-active-instance coordination.
/// The table holds one row per lease key (default: <c>"game-loop"</c>).
/// </summary>
public sealed class LeaderLease
{
    public string AgentToken { get; init; } = string.Empty;

    /// <summary>Logical name of the lease (e.g. <c>"game-loop"</c>).</summary>
    required public string Key { get; init; }

    /// <summary>Opaque identifier of the holder (e.g. a GUID generated at startup).</summary>
    required public string HolderId { get; init; }

    /// <summary>UTC timestamp after which the lease may be taken over by another instance.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
