using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record OrbitShipCommand
{
    public required string ShipSymbol { get; init; }

    /// <summary>
    /// Phase 13c: when true, the command handler skips publishing the continuation
    /// <see cref="ShipAutomationTickEvent"/> after a successful orbit. Set to true by
    /// goal executors that invoke orbit inline and handle continuation themselves.
    /// </summary>
    public bool SuppressContinuationTick { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public OrbitShipCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class OrbitShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<OrbitShipHandler> logger)
{
    public Task Handle(OrbitShipCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(OrbitShipCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "CommandHandler {Handler}: {Command} for ship {Symbol}.",
            nameof(OrbitShipHandler),
            nameof(OrbitShipCommand),
            command.ShipSymbol);

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.Docked)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(OrbitShipCommand),
                "DOCKED",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked before orbiting.");

            logger.LogWarning("Skipping orbit for ship {Symbol}: expected DOCKED but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        var nav = await port.OrbitShipAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, nav, null, cancellationToken);

        var publishedAt = TimeProvider.System.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(nav.WaypointSymbol) && !command.SuppressContinuationTick)
        {
            // Phase 13b: ShipInOrbitEvent deleted (Tier 3); publish only the automation tick.
            // Phase 13c: suppressed when called inline from a goal executor (SuppressContinuationTick=true)
            // because the executor continues directly and ShipGoalExecutorService publishes its own GoalStep tick.
            await bus.PublishAsync(new ShipAutomationTickEvent(
                command.ShipSymbol,
                "Undocked",
                publishedAt,
                Guid.NewGuid(),
                Guid.Empty));
        }

        logger.LogInformation(
            "CommandHandler {Handler}: {Command} handled; Ship {Symbol} in orbit at {Waypoint}.",
            nameof(OrbitShipHandler),
            nameof(OrbitShipCommand),
            command.ShipSymbol,
            nav.WaypointSymbol);

        return new ShipCommandResult(
            command.ShipSymbol,
            ShipLocalStatusMapper.FromApiStatus(nav.Status),
            nav.SystemSymbol,
            nav.WaypointSymbol,
            ArrivesAt: nav.ArrivesAt ?? default,
            FuelCurrent: ship?.FuelCurrent ?? 0,
            FuelCapacity: ship?.FuelCapacity ?? 0,
            CargoCurrent: ship?.CargoCurrent ?? 0,
            CargoCapacity: ship?.CargoCapacity ?? 0);
    }
}
