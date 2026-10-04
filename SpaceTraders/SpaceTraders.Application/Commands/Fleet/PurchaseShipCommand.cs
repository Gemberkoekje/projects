using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using Wolverine;

namespace SpaceTraders.Application.Commands.Fleet;

/// <summary>
/// Purchases a ship of a given type at a specific shipyard, subject to a credit reserve check.
/// </summary>
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

/// <summary>Buys through <see cref="ShipPurchaseService"/>, which also publishes the purchase.</summary>
public sealed class PurchaseShipHandler(
    IShipPurchaseService shipPurchases,
    ILogger<PurchaseShipHandler> logger)
{
    public PurchaseShipHandler(
        ISpaceTradersPort port,
        IAgentRepository agents,
        IShipRepository ships,
        IShipyardRepository shipyards,
        IBudgetPolicy budget,
        ShipyardCalls calls,
        PurchaseNeeds purchases,
        IShipNameBook names,
        IMessageBus bus,
        ILogger<PurchaseShipHandler> logger)
        : this(new ShipPurchaseService(port, agents, ships, shipyards, budget, calls, purchases, names, bus, Microsoft.Extensions.Logging.Abstractions.NullLogger<ShipPurchaseService>.Instance), logger)
    {
    }

    public async Task Handle(PurchaseShipCommand command, CancellationToken cancellationToken)
    {
        var purchase = await shipPurchases.TryPurchaseAsync(command.ShipType, command.ShipyardWaypoint, cancellationToken);
        if (!purchase.IsSuccess || purchase.PurchasedShip is null)
        {
            logger.LogInformation(
                "PurchaseShipHandler: skipping {ShipType} at {Shipyard} — {Reason}",
                command.ShipType,
                command.ShipyardWaypoint,
                purchase.FailureReason ?? "Purchase failed.");
            return;
        }

        logger.LogInformation(
            "PurchaseShipHandler: purchased {ShipSymbol} ({Type}) for {Cost} credits.",
            purchase.PurchasedShip.Symbol,
            command.ShipType,
            purchase.ActualCost);
    }
}
