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
///  - Ensures the single scout-all-marketplaces plan exists.
///  - Executes active scout and contract assignments.
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
        var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
        var scoutPlanBootstrap = scope.ServiceProvider.GetRequiredService<IScoutAllMarketplacesPlanService>();
        var contractPlanBootstrap = scope.ServiceProvider.GetRequiredService<IContractPlanService>();
        var probeDeploymentPlanBootstrap = scope.ServiceProvider.GetRequiredService<IProbeDeploymentPlanService>();
        var miningAutomation = scope.ServiceProvider.GetRequiredService<IMiningAutomationService>();
        var tradingAutomation = scope.ServiceProvider.GetRequiredService<ITradingAutomationService>();
        var assignments = scope.ServiceProvider.GetRequiredService<IShipAssignmentRepository>();
        var ships = scope.ServiceProvider.GetRequiredService<IShipRepository>();
        var goalExecutor = scope.ServiceProvider.GetRequiredService<IShipGoalExecutorService>();

        await scoutPlanBootstrap.EnsureBootstrappedAsync(cancellationToken);
        await contractPlanBootstrap.EnsureBootstrappedAsync(cancellationToken);
        await probeDeploymentPlanBootstrap.EnsureBootstrappedAsync(cancellationToken);
        await miningAutomation.EnsureBootstrappedAsync(cancellationToken);
        await tradingAutomation.EnsureBootstrappedAsync(cancellationToken);
        await ExecuteActiveScoutAssignmentsAsync(assignments, goalExecutor, cancellationToken);
        await ExecuteActiveContractAssignmentsAsync(assignments, ships, bus, cancellationToken);
        await PublishApiAvailabilityEventsAsync(bus, cancellationToken);
    }

    private static async Task ExecuteActiveScoutAssignmentsAsync(
        IShipAssignmentRepository assignments,
        IShipGoalExecutorService goalExecutor,
        CancellationToken cancellationToken)
    {
        var activeAssignments = await assignments.GetAllActiveAsync(cancellationToken);

        foreach (var assignment in activeAssignments)
        {
            if (assignment.CompletedAt.HasValue
                || !string.Equals(assignment.AssignmentType, "Scout", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await goalExecutor.ExecuteAsync(assignment.ShipSymbol, cancellationToken);
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
