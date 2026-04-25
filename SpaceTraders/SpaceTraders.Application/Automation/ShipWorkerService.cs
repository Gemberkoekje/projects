using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Drives the Trade assignment state machine for each ship.
/// Each iteration picks up the next step of any active Trade assignment and dispatches
/// the appropriate command. Steps are persisted via <see cref="IShipAssignmentRepository"/>
/// so the machine resumes after a pod restart.
///
/// Trade assignment steps (StepIndex):
///   0 – Navigate to buy waypoint
///   1 – Dock at buy waypoint
///   2 – Buy cargo
///   3 – Navigate to sell waypoint
///   4 – Dock at sell waypoint
///   5 – Sell cargo  →  complete (sets CompletedAt)
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

        // Get all ships to check which are ready to proceed
        var allShips = await ships.GetAllAsync(cancellationToken);

        foreach (var ship in allShips)
        {
            // Skip ships that are still in transit
            if (ship.IsInTransit) continue;

            var assignment = await assignments.FindAsync(ship.Symbol, cancellationToken);
            if (assignment is null || assignment.CompletedAt.HasValue) continue;
            if (!assignment.AssignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase)) continue;

            await AdvanceTradeAssignmentAsync(ship, assignment, assignments, bus, cancellationToken);
        }
    }

    private async Task AdvanceTradeAssignmentAsync(
        ShipModel ship,
        DTOs.ShipAssignmentDto assignment,
        IShipAssignmentRepository assignments,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (assignment.StepIndex)
            {
                case 0: // Navigate to buy waypoint
                    if (assignment.OriginWaypoint is null)
                    {
                        logger.LogWarning("Ship {Symbol} Trade assignment missing OriginWaypoint. Marking complete.", ship.Symbol);
                        await CompleteAssignmentAsync(assignment, assignments, cancellationToken);
                        return;
                    }

                    if (!IsAtWaypoint(ship, assignment.OriginWaypoint))
                    {
                        await bus.SendAsync(new NavigateShipCommand(ship.Symbol, assignment.OriginWaypoint));
                    }
                    await AdvanceStepAsync(assignment, assignments, cancellationToken);
                    break;

                case 1: // Dock at buy waypoint
                    if (IsAtWaypoint(ship, assignment.OriginWaypoint) && !IsDocked(ship))
                        await bus.SendAsync(new DockShipCommand(ship.Symbol));
                    await AdvanceStepAsync(assignment, assignments, cancellationToken);
                    break;

                case 2: // Buy cargo
                    if (assignment.CargoSymbol is not null && IsDocked(ship))
                    {
                        var units = ship.CargoCapacity - ship.CargoCurrent;
                        if (units > 0)
                            await bus.SendAsync(new BuyCargoCommand(ship.Symbol, assignment.CargoSymbol, units));
                    }
                    await AdvanceStepAsync(assignment, assignments, cancellationToken);
                    break;

                case 3: // Navigate to sell waypoint
                    if (assignment.DestWaypoint is null)
                    {
                        logger.LogWarning("Ship {Symbol} Trade assignment missing DestWaypoint. Marking complete.", ship.Symbol);
                        await CompleteAssignmentAsync(assignment, assignments, cancellationToken);
                        return;
                    }

                    if (!IsAtWaypoint(ship, assignment.DestWaypoint))
                    {
                        // Orbit first if docked before navigating
                        if (IsDocked(ship))
                            await bus.SendAsync(new OrbitShipCommand(ship.Symbol));
                        else
                            await bus.SendAsync(new NavigateShipCommand(ship.Symbol, assignment.DestWaypoint));
                    }
                    await AdvanceStepAsync(assignment, assignments, cancellationToken);
                    break;

                case 4: // Dock at sell waypoint
                    if (IsAtWaypoint(ship, assignment.DestWaypoint) && !IsDocked(ship))
                        await bus.SendAsync(new DockShipCommand(ship.Symbol));
                    await AdvanceStepAsync(assignment, assignments, cancellationToken);
                    break;

                case 5: // Sell all cargo
                    if (assignment.CargoSymbol is not null && ship.CargoCurrent > 0 && IsDocked(ship))
                        await bus.SendAsync(new SellCargoCommand(ship.Symbol, assignment.CargoSymbol, ship.CargoCurrent));
                    await CompleteAssignmentAsync(assignment, assignments, cancellationToken);
                    logger.LogInformation("Ship {Symbol} completed Trade assignment.", ship.Symbol);
                    break;

                default:
                    logger.LogWarning("Ship {Symbol} has unexpected Trade StepIndex {Step}. Marking complete.", ship.Symbol, assignment.StepIndex);
                    await CompleteAssignmentAsync(assignment, assignments, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error advancing Trade assignment for ship {Symbol} at step {Step}.",
                ship.Symbol, assignment.StepIndex);
        }
    }

    private static bool IsAtWaypoint(ShipModel ship, string? waypointSymbol)
        => waypointSymbol is not null &&
           ship.WaypointSymbol?.Equals(waypointSymbol, StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsDocked(ShipModel ship)
        => ship.Status?.Equals("DOCKED", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task AdvanceStepAsync(
        DTOs.ShipAssignmentDto assignment,
        IShipAssignmentRepository assignments,
        CancellationToken cancellationToken)
    {
        var updated = assignment with { StepIndex = assignment.StepIndex + 1 };
        await assignments.UpsertAsync(updated, cancellationToken);
    }

    private static async Task CompleteAssignmentAsync(
        DTOs.ShipAssignmentDto assignment,
        IShipAssignmentRepository assignments,
        CancellationToken cancellationToken)
    {
        var completed = assignment with { CompletedAt = DateTimeOffset.UtcNow };
        await assignments.UpsertAsync(completed, cancellationToken);
    }
}
