namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class ScoutPlanStateRecord
{
    public string AgentToken { get; init; } = string.Empty;

    public Guid PlanId { get; set; }

    required public string ShipSymbol { get; set; }

    required public string StartWaypointSymbol { get; set; }

    required public string RouteWaypointSymbolsJson { get; set; }

    public int CurrentRouteIndex { get; set; }

    public int Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
