using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.Persistence.Scoping;

/// <summary>
/// The active agent's reset date (slice 2.13, D70), read from its id at every call: empty until agent bootstrap has picked
/// the agent, and the same for the rest of the process, as a server reset ends it.
/// </summary>
/// <param name="agent">The active agent.</param>
public sealed class ActiveReset(IAgentDataScope agent) : IActiveReset
{
    /// <inheritdoc />
    public string ResetDate => AgentIdentity.ResetDateOf(agent.AgentId);
}
