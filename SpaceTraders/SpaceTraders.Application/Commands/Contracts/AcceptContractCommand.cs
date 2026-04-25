using Microsoft.Extensions.Logging;
using SpaceTraders.Domain.Events;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using Wolverine;

namespace SpaceTraders.Application.Commands.Contracts;

public record AcceptContractCommand(string ContractId);

public sealed class AcceptContractHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    IMessageBus bus,
    ILogger<AcceptContractHandler> logger)
{
    public async Task Handle(AcceptContractCommand command, CancellationToken cancellationToken)
    {
        var result = await apiClient.AcceptContractAsync(command.ContractId, cancellationToken);

        var cached = await db.Contracts.FindAsync([command.ContractId], cancellationToken);
        if (cached is not null)
        {
            cached.IsAccepted = result.Contract.Accepted;
            cached.IsFulfilled = result.Contract.Fulfilled;
            cached.LastSyncedAt = DateTimeOffset.UtcNow;
        }

        if (result.Agent is not null)
        {
            var cachedAgent = await db.Agents.FindAsync([result.Agent.Symbol], cancellationToken);
            if (cachedAgent is not null)
            {
                cachedAgent.Credits = result.Agent.Credits;
                cachedAgent.LastSyncedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await bus.PublishAsync(new ContractAcceptedEvent(command.ContractId));
        logger.LogInformation("Contract {ContractId} accepted.", command.ContractId);
    }
}
