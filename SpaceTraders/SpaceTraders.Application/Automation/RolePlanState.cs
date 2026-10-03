using SpaceTraders.Application.Roles;

namespace SpaceTraders.Application.Automation;

public static partial class PlanTypes
{
    public const string Roles = "Roles";
}

/// <summary>
/// The role board's view after its last evaluation (PLAN.md slice 6.9): every ship's role, why it has it, and what each
/// role it could take would earn per hour. Written at each evaluation; the plans read the roles from it.
/// </summary>
public sealed record RolePlanState
{
    /// <summary>When the roles were last weighed.</summary>
    public required DateTimeOffset EvaluatedAt { get; init; }

    /// <summary>
    /// What the evaluation weighed besides the ships: the plans that were on, and whether the contract wanted ore. A
    /// change weighs the roles again at once.
    /// </summary>
    public string Conditions { get; init; } = string.Empty;

    /// <summary>The ships, by symbol; probes are left out.</summary>
    public required IReadOnlyList<RoleShipState> Ships { get; init; }
}

/// <summary>One ship on the role board.</summary>
public sealed record RoleShipState
{
    public required string ShipSymbol { get; init; }

    public required FleetRole Role { get; init; }

    /// <summary>Why it has the role: <c>only_role</c>, <c>survey_first</c>, <c>contract</c>, <c>most_profitable</c>, <c>no_work</c> or <c>no_role</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>Since when it has the role.</summary>
    public required DateTimeOffset Since { get; init; }

    /// <summary>For a role chosen by profit: the trip that decided it.</summary>
    public string Job { get; init; } = string.Empty;

    /// <summary>For a role chosen by profit: what that trip earns per hour, by the estimate.</summary>
    public long CreditsPerHour { get; init; }

    /// <summary>For each role it could take but surveying: its best trip and what it earns per hour.</summary>
    public IReadOnlyList<RoleEstimateState> Estimates { get; init; } = [];

    /// <summary>How fast it fills its hold, as its mining and siphon estimates took it; kept across a restart.</summary>
    public IReadOnlyList<RoleRateState> Rates { get; init; } = [];
}

/// <summary>What one role would earn a ship per hour: its best trip, by the estimate.</summary>
public sealed record RoleEstimateState
{
    public required FleetRole Role { get; init; }

    public required long CreditsPerHour { get; init; }

    public string Job { get; init; } = string.Empty;
}

/// <summary>How fast a ship fills its hold one way: units per extraction or siphon, and seconds per one.</summary>
public sealed record RoleRateState
{
    public required GatheringKind Kind { get; init; }

    public required double UnitsPerAction { get; init; }

    public required double SecondsPerAction { get; init; }

    /// <summary>Whether extractions showed it; false for the default.</summary>
    public required bool Observed { get; init; }
}
