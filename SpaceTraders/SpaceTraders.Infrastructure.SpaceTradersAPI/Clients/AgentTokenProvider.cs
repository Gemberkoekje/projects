namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

public sealed class AgentTokenProvider : IAgentTokenProvider
{
    public string? Token { get; private set; }

    public void Set(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Token = token;
    }
}
