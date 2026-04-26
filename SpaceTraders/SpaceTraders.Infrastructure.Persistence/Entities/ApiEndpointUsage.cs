namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class ApiEndpointUsage
{
    public long Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    public required string HttpMethod { get; init; }

    public required string Endpoint { get; init; }

    public int Calls { get; set; }

    public DateTimeOffset LastCalledAt { get; set; }
}
