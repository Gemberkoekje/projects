using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Background service.
/// Every 5 s:
///  - Skips processing if this instance is not the leader (see <see cref="ILeaderElection"/>).
///  - With automation switched on (<see cref="AutomationSwitches"/>): bootstraps each plan that is
///    switched on, runs one goal step per ship, and runs the contract plan's assignments.
///  - Detects API availability transitions and publishes ApiUnavailableEvent / ApiAvailableEvent.
/// </summary>
public sealed class GameLoopService(
    IServiceScopeFactory serviceScopeFactory,
    IApiAvailabilityState apiAvailability,
    ILeaderElection leaderElection,
    ILogger<GameLoopService> logger) : BackgroundService
{
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
        var services = scope.ServiceProvider;
        var bus = services.GetRequiredService<Wolverine.IMessageBus>();
        var settings = services.GetRequiredService<ISettingsRepository>();

        if (await settings.IsAutomationEnabledAsync(cancellationToken))
        {
            await RunAutomationAsync(services, settings, bus, cancellationToken);
        }
        else
        {
            logger.LogDebug("GameLoopService: automation is switched off; no plans, goal steps or contract work this tick.");
        }

        await PublishApiAvailabilityEventsAsync(bus, cancellationToken);
    }

    private static async Task RunAutomationAsync(
        IServiceProvider services,
        ISettingsRepository settings,
        Wolverine.IMessageBus bus,
        CancellationToken cancellationToken)
    {
        // A plan that is switched off is not bootstrapped, so it doesn't buy anything either.
        (AutomationPlan Plan, Func<Task> Bootstrap)[] bootstraps =
        [
            (AutomationPlan.Scout, () => services.GetRequiredService<IScoutAllMarketplacesPlanService>().EnsureBootstrappedAsync(cancellationToken)),
            (AutomationPlan.Contract, () => services.GetRequiredService<IContractPlanService>().EnsureBootstrappedAsync(cancellationToken)),
            (AutomationPlan.ProbeDeployment, () => services.GetRequiredService<IProbeDeploymentPlanService>().EnsureBootstrappedAsync(cancellationToken)),
            (AutomationPlan.Mining, () => services.GetRequiredService<IMiningAutomationService>().EnsureBootstrappedAsync(cancellationToken)),
            (AutomationPlan.Trading, () => services.GetRequiredService<ITradingAutomationService>().EnsureBootstrappedAsync(cancellationToken)),
        ];

        foreach (var (plan, bootstrap) in bootstraps)
        {
            if (await settings.IsPlanEnabledAsync(plan, cancellationToken))
            {
                await bootstrap();
            }
        }

        var ships = services.GetRequiredService<IShipRepository>();
        await ExecuteAllShipGoalsAsync(ships, services.GetRequiredService<IShipGoalExecutorService>(), cancellationToken);

        if (await settings.IsPlanEnabledAsync(AutomationPlan.Contract, cancellationToken))
        {
            await ExecuteActiveContractAssignmentsAsync(services.GetRequiredService<IShipAssignmentRepository>(), ships, bus, cancellationToken);
        }
    }

    private static async Task ExecuteAllShipGoalsAsync(
        IShipRepository ships,
        IShipGoalExecutorService goalExecutor,
        CancellationToken cancellationToken)
    {
        var allShips = await ships.GetAllAsync(cancellationToken);

        foreach (var ship in allShips)
        {
            await goalExecutor.ExecuteAsync(ship.Symbol, cancellationToken);
        }
    }

    private static async Task ExecuteActiveContractAssignmentsAsync(
        IShipAssignmentRepository assignments,
        IShipRepository ships,
        Wolverine.IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var activeAssignments = await assignments.GetAllActiveAsync(cancellationToken);

        foreach (var assignment in activeAssignments)
        {
            if (assignment.CompletedAt.HasValue
                || !string.Equals(assignment.AssignmentType, "Contract", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(assignment.CargoSymbol)
                || string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
                || string.IsNullOrWhiteSpace(assignment.DestWaypoint)
                || string.IsNullOrWhiteSpace(assignment.ContractId))
            {
                continue;
            }

            var ship = await ships.FindAsync(assignment.ShipSymbol, cancellationToken);
            if (ship is null)
            {
                continue;
            }

            var cargoUnits = ship.CargoInventory?
                .FirstOrDefault(i => i.Symbol.Equals(assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase))?
                .Units ?? 0;

            if (cargoUnits > 0)
            {
                await bus.InvokeAsync(new FulfillContractDeliveryCommand(
                    assignment.ShipSymbol,
                    assignment.ContractId,
                    assignment.CargoSymbol,
                    assignment.DestWaypoint),
                    cancellationToken);
            }
            else
            {
                await bus.InvokeAsync(new MineResourceVolumeCommand(
                    assignment.ShipSymbol,
                    assignment.CargoSymbol,
                    assignment.OriginWaypoint,
                    assignment.RequiredUnits),
                    cancellationToken);
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
