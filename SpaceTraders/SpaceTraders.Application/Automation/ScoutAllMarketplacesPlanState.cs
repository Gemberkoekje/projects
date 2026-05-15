namespace SpaceTraders.Application.Automation;

public static class PlanTypes
{
    public const string ScoutAllMarketplaces = "ScoutAllMarketplaces";
    public const string ContractMineral = "ContractMineral";
    public const string ProbeDeployment = "ProbeDeployment";
}

public enum ScoutPlanStatus
{
    None = 0,
    Active = 1,
    Completed = 2,
}

public sealed record ScoutAllMarketplacesPlanState
{
    public required Guid PlanId { get; init; }

    public required string ShipSymbol { get; init; }

    public required string StartWaypointSymbol { get; init; }

    public required IReadOnlyList<string> RouteWaypointSymbols { get; init; }

    public required int CurrentRouteIndex { get; init; }

    public required ScoutPlanStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
