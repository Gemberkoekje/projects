using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record JumpShipCommand
{
    public required string ShipSymbol { get; init; }

    public required string DestinationSystem { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public JumpShipCommand(string ShipSymbol, string DestinationSystem)
    {
        this.ShipSymbol = ShipSymbol;
        this.DestinationSystem = DestinationSystem;
    }
}

public sealed class JumpShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IMessageBus bus,
    IJumpGateCacheService jumpGates,
    ILogger<JumpShipHandler> logger)
{
    private sealed class NoopJumpGateCacheService : IJumpGateCacheService
    {
        public Task TryRefreshAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public JumpShipHandler(
        ISpaceTradersPort port,
        IShipRepository ships,
        IMessageBus bus,
        ILogger<JumpShipHandler> logger)
        : this(port, ships, bus, new NoopJumpGateCacheService(), logger)
    {
    }

    public async Task Handle(JumpShipCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (!string.Equals(ship?.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(JumpShipCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit to jump.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping jump for ship {Symbol}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return;
        }

        if (!string.IsNullOrWhiteSpace(ship?.SystemSymbol) && !string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            await jumpGates.TryRefreshAsync(ship.SystemSymbol, ship.WaypointSymbol, cancellationToken);
        }

        var nowJump = TimeProvider.System.GetUtcNow();
        var result = await port.JumpShipAsync(command.ShipSymbol, command.DestinationSystem, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, result.Nav, null, cancellationToken);

        var inTransitEvent = new ShipInTransitEvent(
            command.ShipSymbol,
            ship?.WaypointSymbol ?? result.Nav.WaypointSymbol,
            result.Nav.DestWaypointSymbol ?? command.DestinationSystem,
            result.Nav.ArrivesAt ?? nowJump,
            Guid.Empty,
            Guid.Empty,
            nowJump);

        await bus.PublishAsync(inTransitEvent);

        logger.LogInformation("Ship {Symbol} jumping to system {System}, cooldown {Seconds}s.",
            command.ShipSymbol, command.DestinationSystem, result.CooldownSeconds);
    }
}
