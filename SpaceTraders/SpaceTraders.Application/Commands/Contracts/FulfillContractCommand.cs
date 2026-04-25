using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Contracts;

public record FulfillContractCommand(string ContractId);

public sealed class FulfillContractHandler(
    ISpaceTradersPort port,
    IContractRepository contracts,
    IAgentRepository agents,
    IMessageBus bus,
    ILogger<FulfillContractHandler> logger)
{
    public async Task Handle(FulfillContractCommand command, CancellationToken cancellationToken)
    {
        var result = await port.FulfillContractAsync(command.ContractId, cancellationToken);

        await contracts.UpdateStatusAsync(result.ContractId, result.IsAccepted, result.IsFulfilled, cancellationToken);

        if (result.AgentSymbol is not null && result.AgentCredits.HasValue)
        {
            var agent = await agents.GetAsync(cancellationToken);
            if (agent is not null)
                await agents.UpsertAsync(agent with { Credits = result.AgentCredits.Value }, cancellationToken);
        }

        var credits = result.AgentCredits ?? 0;
        await bus.PublishAsync(new ContractFulfilledEvent(command.ContractId, credits));
        logger.LogInformation("Contract {ContractId} fulfilled. Agent credits: {Credits}.", command.ContractId, credits);
    }
}
