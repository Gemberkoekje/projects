using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Sync;

public sealed class SyncAllShipsHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<SyncAllShipsHandler> logger)
{
    public async Task Handle(SyncAllShipsCommand command, CancellationToken cancellationToken)
    {
        var page = 1;
        const int limit = 20;
        var total = 0;

        while (true)
        {
            var response = await port.GetMyShipsAsync(page, limit, cancellationToken);

            foreach (var ship in response.Items)
                await ships.UpsertAsync(ship, cancellationToken);

            total += response.Items.Count;

            if (response.Items.Count == 0 || total >= response.Total)
                break;

            page++;
        }

        logger.LogInformation("Synced {Count} ships.", total);
    }
}
