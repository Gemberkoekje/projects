namespace SpaceTraders.Infrastructure.Persistence.Scoping;

[Mutable]
public sealed class AgentDataScope : IAgentDataScope
{
    public string Token { get; private set; } = string.Empty;

    public void Set(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Token = token;
    }
}
