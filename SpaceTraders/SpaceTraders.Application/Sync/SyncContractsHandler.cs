using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Sync;

public sealed class SyncContractsHandler(
    ISpaceTradersPort port,
    IContractRepository contracts,
    ILogger<SyncContractsHandler> logger)
{
    public async Task Handle(SyncContractsCommand command, CancellationToken cancellationToken)
    {
        var page = 1;
        const int limit = 20;
        var total = 0;

        while (true)
        {
            var response = await port.GetMyContractsAsync(page, limit, cancellationToken);

            foreach (var c in response.Items)
                await contracts.UpsertAsync(new ContractDto(c.Id, c.FactionSymbol, c.Type, c.IsAccepted, c.IsFulfilled, c.Expiration, c.DeadlineToAccept), cancellationToken);

            total += response.Items.Count;

            if (response.Items.Count == 0 || total >= response.Total)
                break;

            page++;
        }

        logger.LogInformation("Synced {Count} contracts.", total);
    }
}
