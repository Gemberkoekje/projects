using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Stores the agent's credits after an API call that changed them, and publishes
/// <see cref="AgentCreditsChangedEvent"/> when they did (B7). The credit samples, the in-memory
/// credit history and the probe plan's wait for credits all listen to that event.
/// </summary>
public static class AgentCreditsUpdates
{
    /// <summary>Sets the cached agent's credits; nothing happens while no agent is cached.</summary>
    public static async Task SetCreditsAsync(this IAgentRepository agents, IMessageBus bus, long credits, CancellationToken cancellationToken)
    {
        var agent = await agents.GetAsync(cancellationToken);
        if (agent is null)
        {
            return;
        }

        await agents.UpsertAsync(agent with { Credits = credits }, cancellationToken);
        await PublishIfChangedAsync(bus, agent.Credits, credits);
    }

    /// <summary>Stores the agent as an API response returned it (a ship purchase also changes its ship count).</summary>
    public static async Task SetAgentAsync(this IAgentRepository agents, IMessageBus bus, AgentModel agent, CancellationToken cancellationToken)
    {
        var previous = await agents.GetAsync(cancellationToken);
        await agents.UpsertAsync(agent, cancellationToken);
        if (previous is not null)
        {
            await PublishIfChangedAsync(bus, previous.Credits, agent.Credits);
        }
    }

    private static async Task PublishIfChangedAsync(IMessageBus bus, long oldCredits, long newCredits)
    {
        if (oldCredits != newCredits)
        {
            await bus.PublishAsync(new AgentCreditsChangedEvent(oldCredits, newCredits));
        }
    }
}
