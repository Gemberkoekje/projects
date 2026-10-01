namespace SpaceTraders.Infrastructure.Persistence.Entities;

[Mutable]
public sealed class PlanStateRecord
{
    public string AgentId { get; init; } = string.Empty;

    required public string PlanType { get; init; }

    required public string StateJson { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
