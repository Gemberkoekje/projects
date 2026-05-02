using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record SurveyCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SurveyCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class SurveyHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipEventScheduler scheduler,
    ISurveyRepository surveys,
    IMessageBus bus,
    ILogger<SurveyHandler> logger)
{
    public Task Handle(SurveyCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(SurveyCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(SurveyCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit to survey.");

            logger.LogWarning("Skipping survey for ship {Symbol}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        var result = await port.SurveyAsync(command.ShipSymbol, cancellationToken);
        if (!string.IsNullOrWhiteSpace(ship!.WaypointSymbol) && result.Surveys.Count > 0)
        {
            await surveys.UpsertAsync(command.ShipSymbol, result.Surveys, cancellationToken);
        }

        // Phase 14c: persist cooldown and schedule the wake-up event via the persistent scheduler.
        var now = TimeProvider.System.GetUtcNow();
        var cooldownExpiresAt = result.CooldownExpiresAt ?? now.AddSeconds(result.CooldownSeconds);
        await ships.UpdateCooldownAsync(command.ShipSymbol, cooldownExpiresAt, cancellationToken);

        var activeGoal = await goals.GetActiveGoalAsync(command.ShipSymbol, cancellationToken);
        if (activeGoal is not null)
        {
            await scheduler.ScheduleCooldownExpiryAsync(command.ShipSymbol, activeGoal.GoalId, cooldownExpiresAt, cancellationToken);
        }

        logger.LogInformation("Ship {Symbol} completed survey. {Count} surveys found, cooldown expires {Cooldown}.",
            command.ShipSymbol, result.Surveys.Count, cooldownExpiresAt);

        return new ShipCommandResult(
            command.ShipSymbol,
            currentStatus,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol ?? string.Empty,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: ship.CargoCurrent,
            CargoCapacity: ship.CargoCapacity);
    }
}
