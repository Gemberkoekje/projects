using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Dead-reckoning background service.
/// Every 5 s:
///  - Skips processing if this instance is not the leader (see <see cref="ILeaderElection"/>).
///  - Detects ships that have arrived at their destination (ArrivesAt elapsed),
///    updates their nav state in the DB, and publishes <see cref="ShipArrivedEvent"/> for arrival processing.
///  - Detects ships with critically low fuel (≤ 20 %) that are docked and publishes ShipFuelLowEvent.
///  - Detects API availability transitions and publishes ApiUnavailableEvent / ApiAvailableEvent.
/// </summary>
public sealed class GameLoopService(
    IServiceScopeFactory serviceScopeFactory,
    IApiAvailabilityState apiAvailability,
    ILeaderElection leaderElection,
    ILogger<GameLoopService> logger) : BackgroundService
{
    private const double FuelLowThreshold = 0.20;
    private static readonly TimeSpan DeadReckoningInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GameLoopService tick.");
            }

            await Task.Delay(DeadReckoningInterval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!leaderElection.IsLeader)
        {
            logger.LogTrace("GameLoopService: not the leader; skipping tick.");
            return;
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var ships = scope.ServiceProvider.GetRequiredService<IShipRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();

        await ApplyDeadReckoningAsync(ships, bus, cancellationToken);
        await CheckFuelAsync(ships, bus, cancellationToken);
        await PublishApiAvailabilityEventsAsync(bus, cancellationToken);
    }

    private async Task ApplyDeadReckoningAsync(
        IShipRepository ships,
        Wolverine.IMessageBus bus,
        CancellationToken cancellationToken)
    {
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
                var now = TimeProvider.System.GetUtcNow();
                var systemSymbol = ExtractSystemSymbol(arrivedWaypoint);
                var arrivedEvent = new ShipArrivedEvent(
                    ship.Symbol,
                    systemSymbol,
                    arrivedWaypoint,
                    now,
                    Guid.NewGuid(),
                    Guid.Empty,
                    now);

                await bus.PublishAsync(arrivedEvent);
                logger.LogInformation("Ship {Symbol} arrived at {Waypoint} (dead-reckoning); emitting ShipArrivedEvent.", ship.Symbol, arrivedWaypoint);
            }
        }
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }

    private async Task CheckFuelAsync(
        IShipRepository ships,
        Wolverine.IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var allShips = await ships.GetAllAsync(cancellationToken);

        foreach (var ship in allShips)
        {
            if (ship.IsInTransit) continue;
            if (ship.FuelCapacity == 0) continue;
            if (!string.Equals(ship.Status, "DOCKED", StringComparison.OrdinalIgnoreCase)) continue;

            var fuelRatio = (double)ship.FuelCurrent / ship.FuelCapacity;
            if (fuelRatio <= FuelLowThreshold)
            {
                logger.LogInformation(
                    "Ship {Symbol} fuel low ({Current}/{Capacity}) while docked; publishing ShipFuelLowEvent.",
                    ship.Symbol, ship.FuelCurrent, ship.FuelCapacity);
                await bus.PublishAsync(new ShipFuelLowEvent(ship.Symbol, ship.FuelCurrent, ship.FuelCapacity));
            }
        }
    }

    private async Task PublishApiAvailabilityEventsAsync(
        Wolverine.IMessageBus bus,
        CancellationToken cancellationToken)
    {
        if (apiAvailability.ConsumeUnavailableTransition())
        {
            logger.LogWarning("SpaceTraders API became unavailable; publishing ApiUnavailableEvent.");
            await bus.PublishAsync(new ApiUnavailableEvent(TimeProvider.System.GetUtcNow()));
        }

        if (apiAvailability.ConsumeAvailableTransition())
        {
            logger.LogInformation("SpaceTraders API became available again; publishing ApiAvailableEvent.");
            await bus.PublishAsync(new ApiAvailableEvent(TimeProvider.System.GetUtcNow()));
        }
    }
}
