using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Sync;

public sealed class SyncAgentHandler(
    ISpaceTradersPort port,
    IAgentRepository agents,
    ILogger<SyncAgentHandler> logger)
{
    public async Task Handle(SyncAgentCommand command, CancellationToken cancellationToken)
    {
        var agent = await port.GetMyAgentAsync(cancellationToken);
        await agents.UpsertAsync(agent, cancellationToken);
        logger.LogInformation("Agent {Symbol} synced. Credits: {Credits}", agent.Symbol, agent.Credits);
    }
}
