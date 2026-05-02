using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record ExtractResourcesCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ExtractResourcesCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class ExtractResourcesHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipEventScheduler scheduler,
    IWaypointRepository waypoints,
    ISurveyRepository surveys,
    IShipAssignmentRepository assignments,
    IMessageBus bus,
    ILogger<ExtractResourcesHandler> logger)
{
    public Task Handle(ExtractResourcesCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(ExtractResourcesCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "CommandHandler {Handler}: {Command} for ship {Symbol}.",
            nameof(ExtractResourcesHandler),
            nameof(ExtractResourcesCommand),
            command.ShipSymbol);

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(ExtractResourcesCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit before extraction.");

            logger.LogWarning("Skipping extract for ship {Symbol}: expected IN_ORBIT but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(ship!.WaypointSymbol))
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(ExtractResourcesCommand),
                "IN_ORBIT_AT_EXTRACTABLE_WAYPOINT",
                "IN_ORBIT_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate extraction location.");

            logger.LogWarning("Skipping extract for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                string.Empty);
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        var waypointType = waypoint?.Type ?? string.Empty;
        var isExtractable = waypointType.Contains("ASTEROID", StringComparison.OrdinalIgnoreCase)
            || waypointType.Contains("ENGINEERED_ASTEROID", StringComparison.OrdinalIgnoreCase)
            || waypointType.Contains("DEBRIS_FIELD", StringComparison.OrdinalIgnoreCase);

        if (!isExtractable)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(ExtractResourcesCommand),
                "IN_ORBIT_AT_EXTRACTABLE_WAYPOINT",
                $"IN_ORBIT_AT_{(string.IsNullOrWhiteSpace(waypointType) ? "UNKNOWN" : waypointType)}",
                "Ship is not at an extractable waypoint.");

            logger.LogWarning("Skipping extract for ship {Symbol}: waypoint {Waypoint} type {Type} is not extractable.", command.ShipSymbol, ship.WaypointSymbol, waypointType);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol);
        }

        var assignment = await assignments.FindAsync(command.ShipSymbol, cancellationToken);
        var preferredSymbol = assignment?.CargoSymbol ?? string.Empty;
        var survey = await surveys.GetBestActiveSurveyAsync(ship.WaypointSymbol, preferredSymbol, cancellationToken);

        ExtractionActionResult result;
        if (!string.IsNullOrWhiteSpace(survey.Signature))
        {
            result = await port.ExtractWithSurveyAsync(command.ShipSymbol, survey, cancellationToken);
            logger.LogInformation("Ship {Symbol} extracted with survey {Signature} targeting {PreferredSymbol}.",
                command.ShipSymbol,
                survey.Signature,
                string.IsNullOrWhiteSpace(preferredSymbol) ? "best available deposit" : preferredSymbol);
        }
        else
        {
            result = await port.ExtractResourcesAsync(command.ShipSymbol, cancellationToken);
        }

        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);

        // Phase 14c: persist cooldown and schedule the wake-up event via the persistent scheduler.
        var now = TimeProvider.System.GetUtcNow();
        var cooldownExpiresAt = result.CooldownExpiresAt ?? now.AddSeconds(result.CooldownSeconds);
        await ships.UpdateCooldownAsync(command.ShipSymbol, cooldownExpiresAt, cancellationToken);

        var activeGoal = await goals.GetActiveGoalAsync(command.ShipSymbol, cancellationToken);
        if (activeGoal is not null)
        {
            await scheduler.ScheduleCooldownExpiryAsync(command.ShipSymbol, activeGoal.GoalId, cooldownExpiresAt, cancellationToken);
        }

        logger.LogInformation(
            "CommandHandler {Handler}: {Command} handled; Ship {Symbol} extracted {Units}x {Good}. Cooldown expires: {Cooldown}.",
            nameof(ExtractResourcesHandler),
            nameof(ExtractResourcesCommand),
            command.ShipSymbol,
            result.YieldUnits,
            result.YieldSymbol,
            cooldownExpiresAt);

        return new ShipCommandResult(
            command.ShipSymbol,
            currentStatus,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: result.Cargo.Units,
            CargoCapacity: result.Cargo.Capacity);
    }
}
