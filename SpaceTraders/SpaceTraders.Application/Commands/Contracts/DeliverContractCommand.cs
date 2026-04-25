using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;

namespace SpaceTraders.Application.Commands.Contracts;

public record DeliverContractCommand(string ContractId, string ShipSymbol, string TradeSymbol, int Units);

public sealed class DeliverContractHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    ILogger<DeliverContractHandler> logger)
{
    public async Task Handle(DeliverContractCommand command, CancellationToken cancellationToken)
    {
        var result = await apiClient.DeliverContractAsync(command.ContractId, new DeliverContractRequest
        {
            ShipSymbol = command.ShipSymbol,
            TradeSymbol = command.TradeSymbol,
            Units = command.Units
        }, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var cachedContract = await db.Contracts.FindAsync([command.ContractId], cancellationToken);
        if (cachedContract is not null)
        {
            cachedContract.IsAccepted = result.Contract.Accepted;
            cachedContract.IsFulfilled = result.Contract.Fulfilled;
            cachedContract.LastSyncedAt = now;
        }

        var cachedShip = await db.Ships.FindAsync([command.ShipSymbol], cancellationToken);
        if (cachedShip is not null)
        {
            cachedShip.CargoCurrent = result.Cargo.Units;
            cachedShip.CargoCapacity = result.Cargo.Capacity;
            cachedShip.LastSyncedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Ship {Symbol} delivered {Units}x {Good} for contract {ContractId}.",
            command.ShipSymbol, command.Units, command.TradeSymbol, command.ContractId);
    }
}
