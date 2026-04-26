using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Fleet;

public sealed record PurchaseShipCommand
{
    public required string ShipType { get; init; }

    public required string ShipyardWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PurchaseShipCommand(string ShipType, string ShipyardWaypoint)
    {
        this.ShipType = ShipType;
        this.ShipyardWaypoint = ShipyardWaypoint;
    }
}

public sealed class PurchaseShipHandler(
    ISpaceTradersPort port,
    IAgentRepository agents,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<PurchaseShipHandler> logger)
{
    public async Task Handle(PurchaseShipCommand command, CancellationToken cancellationToken)
    {
        var systemSymbol = ExtractSystemSymbol(command.ShipyardWaypoint);
        await bus.SendAsync(new RefreshShipyardDataCommand(systemSymbol, command.ShipyardWaypoint, ForceRefresh: true));

        var result = await port.PurchaseShipAsync(command.ShipType, command.ShipyardWaypoint, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);

        var newShip = new ShipModel(
            result.ShipSymbol,
            result.ShipNav.SystemSymbol,
            result.ShipNav.WaypointSymbol,
            result.ShipNav.Status,
            result.ShipNav.FlightMode,
            result.ShipFuel.Current,
            result.ShipFuel.Capacity,
            result.ShipNav.ArrivesAt,
            result.ShipNav.DestWaypointSymbol);

        await ships.UpsertAsync(newShip, cancellationToken);

        if (Enum.TryParse<ShipType>(command.ShipType, true, out var shipTypeEnum))
        {
            await bus.PublishAsync(new NewShipPurchasedEvent(result.ShipSymbol, shipTypeEnum, result.Cost));
        }

        logger.LogInformation("Purchased ship {Symbol} of type {Type} for {Cost} credits.",
            result.ShipSymbol, command.ShipType, result.Cost);
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}
