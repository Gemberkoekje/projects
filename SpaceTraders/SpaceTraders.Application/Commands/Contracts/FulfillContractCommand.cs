using Microsoft.Extensions.Logging;
using SpaceTraders.Domain.Events;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using Wolverine;

namespace SpaceTraders.Application.Commands.Contracts;

public record FulfillContractCommand(string ContractId);

public sealed class FulfillContractHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    IMessageBus bus,
    ILogger<FulfillContractHandler> logger)
{
    public async Task Handle(FulfillContractCommand command, CancellationToken cancellationToken)
    {
        var result = await apiClient.FulfillContractAsync(command.ContractId, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var cachedContract = await db.Contracts.FindAsync([command.ContractId], cancellationToken);
        if (cachedContract is not null)
        {
            cachedContract.IsAccepted = result.Contract.Accepted;
            cachedContract.IsFulfilled = result.Contract.Fulfilled;
            cachedContract.LastSyncedAt = now;
        }

        var cachedAgent = await db.Agents.FindAsync([result.Agent.Symbol], cancellationToken);
        if (cachedAgent is not null)
        {
            cachedAgent.Credits = result.Agent.Credits;
            cachedAgent.LastSyncedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await bus.PublishAsync(new ContractFulfilledEvent(command.ContractId, result.Agent.Credits));
        logger.LogInformation("Contract {ContractId} fulfilled. Agent credits: {Credits}.", command.ContractId, result.Agent.Credits);
    }
}
