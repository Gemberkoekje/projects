namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class AgentSetting
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public required string Type { get; set; }

    public required string Description { get; set; }
}
