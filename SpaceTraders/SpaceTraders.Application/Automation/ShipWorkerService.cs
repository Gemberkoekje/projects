using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Drives the Trade, Mine, and Scout assignment state machines for each ship.
/// Each iteration picks up the next step of any active assignment and delegates
/// to the appropriate Stateless state machine.  Steps are persisted via
/// <see cref="IShipAssignmentRepository"/> so the machine resumes after a pod restart.
/// </summary>
public sealed class ShipWorkerService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<ShipWorkerService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

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
                logger.LogError(ex, "Error in ShipWorkerService tick.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var assignments = scope.ServiceProvider.GetRequiredService<IShipAssignmentRepository>();
        var ships = scope.ServiceProvider.GetRequiredService<IShipRepository>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var automationEnabled = await settings.GetAsync<bool>("Automation.Enabled", cancellationToken);
        if (!automationEnabled) return;

        var allShips = await ships.GetAllAsync(cancellationToken);

        foreach (var ship in allShips)
        {
            if (ship.IsInTransit) continue;

            var assignment = await assignments.FindAsync(ship.Symbol, cancellationToken);
            if (assignment is null || assignment.CompletedAt.HasValue) continue;

            try
            {
                if (assignment.AssignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
                {
                    var machine = new TradeStateMachine(ship, assignment, assignments, bus, logger);
                    await machine.AdvanceAsync(cancellationToken);
                }
                else if (assignment.AssignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase))
                {
                    var machine = new MineStateMachine(ship, assignment, assignments, bus, logger);
                    await machine.AdvanceAsync(cancellationToken);
                }
                else if (assignment.AssignmentType.Equals("Scout", StringComparison.OrdinalIgnoreCase))
                {
                    var machine = new ScoutStateMachine(ship, assignment, assignments, bus, logger);
                    await machine.AdvanceAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error advancing {Type} assignment for ship {Symbol} at step {Step}.",
                    assignment.AssignmentType, ship.Symbol, assignment.StepIndex);
            }
        }
    }
}
