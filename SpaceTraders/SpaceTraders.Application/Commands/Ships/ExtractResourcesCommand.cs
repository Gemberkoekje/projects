using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.Application.Commands.Ships;

public record ExtractResourcesCommand(string ShipSymbol);

public sealed class ExtractResourcesHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    ILogger<ExtractResourcesHandler> logger)
{
    public async Task Handle(ExtractResourcesCommand command, CancellationToken cancellationToken)
    {
        var result = await apiClient.ExtractResourcesAsync(command.ShipSymbol, cancellationToken);

        var cachedShip = await db.Ships.FindAsync([command.ShipSymbol], cancellationToken);
        if (cachedShip is not null)
        {
            cachedShip.CargoCurrent = result.Cargo.Units;
            cachedShip.CargoCapacity = result.Cargo.Capacity;
            cachedShip.LastSyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Ship {Symbol} extracted {Units}x {Good}. Cooldown: {Cooldown}s.",
            command.ShipSymbol, result.Extraction.Yield.Units, result.Extraction.Yield.Symbol,
            result.Cooldown.TotalSeconds);
    }
}
