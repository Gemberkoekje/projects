using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record ScrapShipCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ScrapShipCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class ScrapShipHandler(
    ISpaceTradersPort port,
    IAgentRepository agents,
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    IMessageBus bus,
    ILogger<ScrapShipHandler> logger)
{
    public Task Handle(ScrapShipCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(ScrapShipCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.Docked)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(ScrapShipCommand),
                "DOCKED_AT_SHIPYARD",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked at a shipyard before scrapping.");

            logger.LogWarning("Skipping scrap for ship {Symbol}: expected DOCKED but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        var systemSymbol = ship!.SystemSymbol ?? string.Empty;
        var waypointSymbol = ship.WaypointSymbol ?? string.Empty;

        var result = await port.ScrapShipAsync(command.ShipSymbol, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);
        await ships.RemoveAsync(command.ShipSymbol, cancellationToken);

        await assignments.UpsertAsync(new SpaceTraders.Application.DTOs.ShipAssignmentDto(
            command.ShipSymbol,
            "Scrapped",
            null,
            null,
            null,
            null,
            0,
            TimeProvider.System.GetUtcNow(),
            TimeProvider.System.GetUtcNow()),
            cancellationToken);

        logger.LogInformation("Scrapped ship {Symbol} for {Value} credits.", command.ShipSymbol, result.Value);

        // After scrap the ship is gone; report the last known status/location.
        return new ShipCommandResult(
            command.ShipSymbol,
            currentStatus,
            systemSymbol,
            waypointSymbol);
    }
}
