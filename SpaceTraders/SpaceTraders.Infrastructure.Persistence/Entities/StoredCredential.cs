namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class StoredCredential
{
    public string AgentToken { get; init; } = string.Empty;

    required public string Key { get; init; }

    required public string Value { get; init; }

    public DateTimeOffset StoredAt { get; init; }
}
