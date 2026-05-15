using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Contracts;

public sealed record AcceptContractCommand
{
    public required string ContractId { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AcceptContractCommand(string ContractId)
    {
        this.ContractId = ContractId;
    }
}

public sealed class AcceptContractHandler(
    ISpaceTradersPort port,
    IContractRepository contracts,
    IAgentRepository agents,
    IMessageBus bus,
    ILogger<AcceptContractHandler> logger)
{
    public async Task Handle(AcceptContractCommand command, CancellationToken cancellationToken)
    {
        var existing = await contracts.FindAsync(command.ContractId, cancellationToken);
        if (existing is not null)
        {
            if (existing.IsAccepted)
            {
                logger.LogInformation("Skipping accept for contract {ContractId}: already accepted.", command.ContractId);
                return;
            }

            if (existing.DeadlineToAccept.HasValue && existing.DeadlineToAccept.Value <= TimeProvider.System.GetUtcNow())
            {
                logger.LogWarning("Skipping accept for contract {ContractId}: acceptance deadline has passed.", command.ContractId);
                return;
            }
        }

        var result = await port.AcceptContractAsync(command.ContractId, cancellationToken);

        await contracts.UpsertAsync(new Application.DTOs.ContractDto(
            result.ContractId,
            result.FactionSymbol,
            result.ContractType,
            result.IsAccepted,
            result.IsFulfilled,
            result.Expiration,
            result.DeadlineToAccept,
            result.TermsDeadline,
            System.Text.Json.JsonSerializer.Serialize(result.Deliverables.Select(d =>
                new Application.DTOs.ContractDeliverableDto(d.TradeSymbol, d.DestinationSymbol, d.UnitsRequired, d.UnitsFulfilled)).ToList())), cancellationToken);

        if (result.AgentSymbol is not null && result.AgentCredits.HasValue)
        {
            var agent = await agents.GetAsync(cancellationToken);
            if (agent is not null)
            {
                await agents.UpsertAsync(agent with { Credits = result.AgentCredits.Value }, cancellationToken);
            }
        }

        await bus.PublishAsync(new ContractAcceptedEvent(command.ContractId));
        logger.LogInformation("Contract {ContractId} accepted.", command.ContractId);
    }
}
