namespace SpaceTraders.Infrastructure.Persistence.Scoping;

[Mutable]
public sealed class AgentDataScope : IAgentDataScope
{
    public string AgentId { get; private set; } = string.Empty;

    public void Set(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        AgentId = agentId;
    }
}
