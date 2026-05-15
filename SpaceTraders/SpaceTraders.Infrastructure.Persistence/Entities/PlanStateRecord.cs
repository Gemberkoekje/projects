namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class PlanStateRecord
{
    public string AgentToken { get; init; } = string.Empty;

    required public string PlanType { get; init; }

    required public string StateJson { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
