using Microsoft.Extensions.Configuration;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.API.Services;

/// <summary>
/// Runs once at startup (after <see cref="StartupSyncService"/>) to resume
/// ships interrupted by a pod restart.
/// </summary>
public sealed class StartupRecoveryService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<StartupRecoveryService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var ships = scope.ServiceProvider.GetRequiredService<IShipRepository>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var now = TimeProvider.System.GetUtcNow();

        var automationEnabled = await settings.GetAsync<bool>("Automation.Enabled", cancellationToken);
        if (!automationEnabled)
        {
            logger.LogInformation("StartupRecovery: automation disabled; skipping ship state recovery.");
            return;
        }

        var goalExecutor = scope.ServiceProvider.GetRequiredService<IShipGoalExecutorService>();

        var fleet = await ships.GetAllAsync(cancellationToken);
        logger.LogInformation("StartupRecovery: recovering {Count} ship(s).", fleet.Count);

        foreach (var ship in fleet)
        {
            await RecoverShipAsync(ship, ships, bus, goalExecutor, now, cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RecoverShipAsync(
        Application.Ports.ShipModel ship,
        IShipRepository ships,
        IMessageBus bus,
        IShipGoalExecutorService goalExecutor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (ship.ArrivesAt.HasValue && ship.ArrivesAt.Value <= now)
        {
            var arrivedWaypoint = ship.DestWaypointSymbol ?? ship.WaypointSymbol ?? string.Empty;

            var transitEvent = new ShipInTransitEvent(
                ship.Symbol,
                ship.WaypointSymbol ?? arrivedWaypoint,
                arrivedWaypoint,
                now,
                Guid.NewGuid(),
                Guid.Empty,
                now);

            await bus.PublishAsync(transitEvent);

            var result = await goalExecutor.ExecuteAsync(ship.Symbol, cancellationToken);
            logger.LogInformation(
                "StartupRecovery: Ship {Symbol} arrived at {Waypoint}; executed goal step (outcome={Outcome}).",
                ship.Symbol, arrivedWaypoint, result?.Outcome);
        }
        else if (ship.ArrivesAt.HasValue)
        {
            var destWaypoint = ship.DestWaypointSymbol ?? ship.WaypointSymbol ?? string.Empty;

            var transitEvent = new ShipInTransitEvent(
                ship.Symbol,
                ship.WaypointSymbol ?? destWaypoint,
                destWaypoint,
                ship.ArrivesAt.Value,
                Guid.NewGuid(),
                Guid.Empty,
                now);

            await bus.PublishAsync(transitEvent);

            logger.LogInformation(
                "StartupRecovery: Ship {Symbol} still in transit (arrives at {ArrivesAt}); emitting ShipInTransitEvent to schedule arrival.",
                ship.Symbol, ship.ArrivesAt.Value);
        }
        else if (ship.LocalStatus == ShipLocalStatus.Docked || ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            var result = await goalExecutor.ExecuteAsync(ship.Symbol, cancellationToken);
            logger.LogInformation(
                "StartupRecovery: Ship {Symbol} is {Status}; executed goal step (outcome={Outcome}).",
                ship.Symbol, ship.LocalStatus, result?.Outcome);
        }
        else
        {
            logger.LogWarning(
                "StartupRecovery: Ship {Symbol} has unrecognised status '{Status}'; skipping.",
                ship.Symbol, ship.Status);
        }
    }
}
