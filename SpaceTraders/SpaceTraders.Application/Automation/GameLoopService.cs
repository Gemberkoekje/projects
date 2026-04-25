using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Dead-reckoning background service.
/// Every 5 s: detects ships that have arrived at their destination (ArrivesAt elapsed),
/// updates their nav state in the DB, and publishes ShipArrivedAtWaypointEvent via Wolverine.
/// </summary>
public sealed class GameLoopService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<GameLoopService> logger) : BackgroundService
{
    private static readonly TimeSpan DeadReckoningInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ApplyDeadReckoningAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in dead-reckoning tick.");
            }

            await Task.Delay(DeadReckoningInterval, stoppingToken);
        }
    }

    private async Task ApplyDeadReckoningAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var ships = scope.ServiceProvider.GetRequiredService<IShipRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();

        var transitShips = await ships.GetInTransitAsync(cancellationToken);

        foreach (var ship in transitShips)
        {
            if (ship.IsInTransit) continue;

            var arrivedWaypoint = ship.DestWaypointSymbol ?? ship.WaypointSymbol;

            var arrivedNav = new NavModel(
                Status: "IN_ORBIT",
                SystemSymbol: ship.SystemSymbol ?? string.Empty,
                WaypointSymbol: arrivedWaypoint ?? ship.WaypointSymbol ?? string.Empty,
                FlightMode: ship.FlightMode ?? "CRUISE",
                DestWaypointSymbol: null,
                ArrivesAt: null);

            await ships.UpdateNavAsync(ship.Symbol, arrivedNav, null, cancellationToken);

            if (arrivedWaypoint is not null)
            {
                await bus.PublishAsync(new ShipArrivedAtWaypointEvent(ship.Symbol, new WaypointSymbol(arrivedWaypoint)));
                logger.LogInformation("Ship {Symbol} arrived at {Waypoint} (dead-reckoning).", ship.Symbol, arrivedWaypoint);
            }
        }
    }
}
