using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Contracts;

public sealed record FulfillContractCommand
{
    public required string ContractId { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public FulfillContractCommand(string ContractId)
    {
        this.ContractId = ContractId;
    }
}

public sealed class FulfillContractHandler(
    ISpaceTradersPort port,
    IContractRepository contracts,
    IAgentRepository agents,
    IMessageBus bus,
    ILogger<FulfillContractHandler> logger)
{
    public async Task Handle(FulfillContractCommand command, CancellationToken cancellationToken)
    {
        var existing = await contracts.FindAsync(command.ContractId, cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsAccepted)
            {
                logger.LogWarning("Skipping fulfill for contract {ContractId}: contract is not accepted.", command.ContractId);
                return;
            }

            if (existing.IsFulfilled)
            {
                logger.LogInformation("Skipping fulfill for contract {ContractId}: already fulfilled.", command.ContractId);
                return;
            }
        }

        var result = await port.FulfillContractAsync(command.ContractId, cancellationToken);

        await contracts.UpsertAsync(new ContractDto(
            result.ContractId,
            result.FactionSymbol,
            result.ContractType,
            result.IsAccepted,
            result.IsFulfilled,
            result.Expiration,
            result.DeadlineToAccept,
            result.TermsDeadline,
            JsonSerializer.Serialize(result.Deliverables.Select(d =>
                new ContractDeliverableDto(d.TradeSymbol, d.DestinationSymbol, d.UnitsRequired, d.UnitsFulfilled)).ToList())), cancellationToken);

        if (result.AgentSymbol is not null && result.AgentCredits.HasValue)
        {
            var agent = await agents.GetAsync(cancellationToken);
            if (agent is not null)
            {
                await agents.UpsertAsync(agent with { Credits = result.AgentCredits.Value }, cancellationToken);
            }
        }

        var credits = result.AgentCredits ?? 0;
        await bus.PublishAsync(new ContractFulfilledEvent(command.ContractId, credits));
        logger.LogInformation("Contract {ContractId} fulfilled. Agent credits: {Credits}.", command.ContractId, credits);
    }
}
