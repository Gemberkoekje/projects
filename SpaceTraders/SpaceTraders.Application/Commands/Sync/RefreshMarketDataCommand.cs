using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Commands.Sync;

public sealed record RefreshMarketDataCommand
{
    public required string SystemSymbol { get; init; }

    public required string WaypointSymbol { get; init; }

    public bool ForceRefresh { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RefreshMarketDataCommand(string SystemSymbol, string WaypointSymbol, bool ForceRefresh = false)
    {
        this.SystemSymbol = SystemSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.ForceRefresh = ForceRefresh;
    }
}

public sealed class RefreshMarketDataHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IMarketRepository markets,
    IMessageBus bus,
    ILogger<RefreshMarketDataHandler> logger)
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public async Task Handle(RefreshMarketDataCommand command, CancellationToken cancellationToken)
    {
        if (!await ships.IsShipAtWaypointAsync(command.WaypointSymbol, cancellationToken))
        {
            logger.LogDebug("Skipping market refresh for {Waypoint}: no ship present.", command.WaypointSymbol);
            return;
        }

        if (!command.ForceRefresh)
        {
            var lastObserved = await markets.GetLastObservedAtAsync(command.WaypointSymbol, cancellationToken);
            if (lastObserved.HasValue && lastObserved.Value + DefaultTtl > TimeProvider.System.GetUtcNow())
            {
                logger.LogDebug("Skipping market refresh for {Waypoint}: data is fresh.", command.WaypointSymbol);
                return;
            }
        }

        var market = await port.GetMarketAsync(command.SystemSymbol, command.WaypointSymbol, cancellationToken);
        await markets.UpsertAsync(market, cancellationToken);
        await bus.PublishAsync(new MarketDataRefreshedEvent(new WaypointSymbol(command.WaypointSymbol)));
        logger.LogInformation("Market data refreshed for {Waypoint}.", command.WaypointSymbol);
    }
}
