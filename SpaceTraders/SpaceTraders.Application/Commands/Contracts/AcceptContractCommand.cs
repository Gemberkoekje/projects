using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Contracts;

public record AcceptContractCommand(string ContractId);

public sealed class AcceptContractHandler(
    ISpaceTradersPort port,
    IContractRepository contracts,
    IAgentRepository agents,
    IMessageBus bus,
    ILogger<AcceptContractHandler> logger)
{
    public async Task Handle(AcceptContractCommand command, CancellationToken cancellationToken)
    {
        var result = await port.AcceptContractAsync(command.ContractId, cancellationToken);

        await contracts.UpdateStatusAsync(result.ContractId, result.IsAccepted, result.IsFulfilled, cancellationToken);

        if (result.AgentSymbol is not null && result.AgentCredits.HasValue)
        {
            var agent = await agents.GetAsync(cancellationToken);
            if (agent is not null)
                await agents.UpsertAsync(agent with { Credits = result.AgentCredits.Value }, cancellationToken);
        }

        await bus.PublishAsync(new ContractAcceptedEvent(command.ContractId));
        logger.LogInformation("Contract {ContractId} accepted.", command.ContractId);
    }
}
