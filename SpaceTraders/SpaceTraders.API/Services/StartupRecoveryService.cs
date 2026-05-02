using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Sync;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Infrastructure.Persistence;
using Wolverine;

namespace SpaceTraders.API.Services;

/// <summary>
/// Runs once at startup (after <see cref="StartupSyncService"/>) to resume
/// ships interrupted by a pod restart.
///
/// Always performs a full API sync to ensure the cached database mirrors the
/// actual SpaceTraders API state before emitting recovery events.
///
/// For each ship:
///  - Ship.ArrivesAt has elapsed  → update nav to IN_ORBIT and emit <see cref="ShipInTransitEvent"/> with arrival time = now
///                                   so the chain immediately routes to in-orbit/undocked.
///  - Ship.ArrivesAt is in the future → emit <see cref="ShipInTransitEvent"/> with the real future arrival time
///                                       so <see cref="ShipInTransitEventHandler"/> schedules the in-orbit event.
///  - Ship is docked               → emit <see cref="ShipAutomationTickEvent"/> to resume the docked chain.
///  - Ship is in orbit             → emit <see cref="ShipAutomationTickEvent"/> to resume the in-orbit/undocked chain.
/// </summary>
public sealed class StartupRecoveryService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<StartupRecoveryService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var syncHandler = scope.ServiceProvider.GetRequiredService<SyncAllShipsHandler>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var ships = scope.ServiceProvider.GetRequiredService<IShipRepository>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var now = TimeProvider.System.GetUtcNow();

        logger.LogInformation("StartupRecovery: syncing ships from API.");
        await syncHandler.Handle(new SyncAllShipsCommand(), cancellationToken);

        var automationEnabled = await settings.GetAsync<bool>("Automation.Enabled", cancellationToken);
        if (!automationEnabled)
        {
            logger.LogInformation("StartupRecovery: automation disabled; skipping ship state recovery.");
            return;
        }

        var fleet = await ships.GetAllAsync(cancellationToken);
        logger.LogInformation("StartupRecovery: recovering {Count} ship(s).", fleet.Count);

        foreach (var ship in fleet)
        {
            await RecoverShipAsync(ship, ships, bus, now, cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RecoverShipAsync(
        Application.Ports.ShipModel ship,
        IShipRepository ships,
        IMessageBus bus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // NOTE: Database is already synced from SyncAllShipsCommand.
        // Recovery only emits events based on current state – no database updates needed.

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

            // Phase 5b: explicit automation tick replaces chain-routed inheritance dispatch.
            await bus.PublishAsync(new ShipAutomationTickEvent(
                ship.Symbol,
                "StartupRecovery: Arrived",
                now,
                transitEvent.CorrelationId,
                transitEvent.EventId));

            logger.LogInformation(
                "StartupRecovery: Ship {Symbol} arrived at {Waypoint}; emitting ShipInTransitEvent (arrival elapsed).",
                ship.Symbol, arrivedWaypoint);
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

            // Phase 5b: schedule an explicit automation tick for the future arrival time.
            await bus.ScheduleAsync(
                new ShipAutomationTickEvent(
                    ship.Symbol,
                    "StartupRecovery: Arriving",
                    ship.ArrivesAt.Value,
                    transitEvent.CorrelationId,
                    transitEvent.EventId),
                ship.ArrivesAt.Value);

            logger.LogInformation(
                "StartupRecovery: Ship {Symbol} still in transit (arrives at {ArrivesAt}); emitting ShipInTransitEvent to schedule in-orbit event.",
                ship.Symbol, ship.ArrivesAt.Value);
        }
        else if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            var correlationId = Guid.NewGuid();
            // Phase 13b: ShipDockedEvent deleted (Tier 3); emit automation tick directly.
            await bus.PublishAsync(new ShipAutomationTickEvent(
                ship.Symbol,
                "StartupRecovery: Docked",
                now,
                correlationId,
                Guid.Empty));

            logger.LogInformation(
                "StartupRecovery: Ship {Symbol} is docked; emitting ShipAutomationTickEvent.",
                ship.Symbol);
        }
        else if (ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            var correlationId = Guid.NewGuid();
            // Phase 13b: ShipInOrbitEvent deleted (Tier 3); emit automation tick directly.
            await bus.PublishAsync(new ShipAutomationTickEvent(
                ship.Symbol,
                "StartupRecovery: InOrbit",
                now,
                correlationId,
                Guid.Empty));

            logger.LogInformation(
                "StartupRecovery: Ship {Symbol} is in orbit; emitting ShipAutomationTickEvent.",
                ship.Symbol);
        }
        else
        {
            logger.LogWarning(
                "StartupRecovery: Ship {Symbol} has unrecognised status '{Status}'; skipping.",
                ship.Symbol, ship.Status);
        }
    }
}
