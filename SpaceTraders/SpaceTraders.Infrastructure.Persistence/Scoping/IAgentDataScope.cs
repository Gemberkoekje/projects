namespace SpaceTraders.Infrastructure.Persistence.Scoping;

/// <summary>The agent whose rows the <see cref="SpaceTradersDbContext"/> reads and writes.</summary>
public interface IAgentDataScope
{
    /// <summary>The <see cref="AgentIdentity"/> of the active agent; empty until agent bootstrap has run.</summary>
    string AgentId { get; }

    void Set(string agentId);
}
