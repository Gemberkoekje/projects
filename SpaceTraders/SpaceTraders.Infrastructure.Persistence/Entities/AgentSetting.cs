namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class AgentSetting
{
    public string AgentToken { get; init; } = string.Empty;

    required public string Key { get; init; }

    required public string Value { get; init; }

    required public string Type { get; init; }

    required public string Description { get; init; }
}
