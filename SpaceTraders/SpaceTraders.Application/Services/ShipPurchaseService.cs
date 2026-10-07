using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Buys ships for every plan, within the credit reserve (<see cref="IBudgetPolicy"/>,
/// <c>FleetExpansion.MinCreditReserve</c>).
/// </summary>
/// <remarks>
/// The API sells a ship only to an agent with a ship at the shipyard (D30). Without one there, the
/// purchase makes no API call: it calls for a ship (<see cref="ShipyardCalls"/>), which the probe plan
/// answers with its nearest free probe, and the plan's next attempt buys. With one there, the price is
/// fetched again first, so the reserve is kept with the price the shipyard asks now: a cached price can be
/// hours old, and every purchase moves it. Whether a plan may buy at all is the purchase order's
/// (<see cref="IPurchaseOrder"/>, D43), which counts each purchase at once (<see cref="PurchaseNeeds"/>): the
/// ledger's row comes a moment later.
/// </remarks>
public sealed class ShipPurchaseService(
    ISpaceTradersPort port,
    IAgentRepository agents,
    IShipRepository ships,
    IShipyardRepository shipyards,
    IBudgetPolicy budget,
    ShipyardCalls calls,
    PurchaseNeeds purchases,
    IShipNameBook names,
    IMessageBus bus,
    ILogger<ShipPurchaseService> logger) : IShipPurchaseService
{
    private const string ProbeShipType = "SHIP_PROBE";
    private const string ScarceSupply = "SCARCE";

    public Task<ShipPurchaseResult> TryPurchaseAsync(
        string shipType,
        string shipyardWaypoint,
        CancellationToken cancellationToken = default)
        => PurchaseAsync(shipType, shipyardWaypoint, unlessScarce: false, cancellationToken);

    /// <inheritdoc />
    public Task<ShipPurchaseResult> TryPurchaseUnlessScarceAsync(
        string shipType,
        string shipyardWaypoint,
        CancellationToken cancellationToken = default)
        => PurchaseAsync(shipType, shipyardWaypoint, unlessScarce: true, cancellationToken);

    /// <summary>The purchase; with <paramref name="unlessScarce"/>, none where the shipyard has the ship SCARCE now (D112).</summary>
    private async Task<ShipPurchaseResult> PurchaseAsync(
        string shipType,
        string shipyardWaypoint,
        bool unlessScarce,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shipType) || string.IsNullOrWhiteSpace(shipyardWaypoint))
        {
            return Failed(ShipPurchaseFailure.InvalidRequest, "Ship type or shipyard waypoint was empty.", 0);
        }

        var cached = await shipyards.FindByWaypointAsync(shipyardWaypoint, cancellationToken);
        var estimatedCost = ResolveShipPurchasePrice(cached, shipType);

        if (cached is null || estimatedCost <= 0)
        {
            return Failed(ShipPurchaseFailure.PriceUnknown, "Purchase price unknown (shipyard not yet visited or stale cache).", estimatedCost);
        }

        var decision = await budget.EvaluateAsync(estimatedCost, cancellationToken);
        if (!decision.CanAfford)
        {
            return Failed(ShipPurchaseFailure.OverBudget, decision.Reason, estimatedCost);
        }

        if (!await HasShipAtAsync(shipyardWaypoint, cancellationToken))
        {
            calls.Call(shipyardWaypoint, shipType, TimeProvider.System.GetUtcNow());
            logger.LogDebug(
                "Ship purchase: no ship of ours at {WaypointSymbol} to buy a {ShipType} there; calling for one (D30).",
                shipyardWaypoint,
                shipType);
            return Failed(
                ShipPurchaseFailure.NoShipAtShipyard,
                $"No ship of ours at {shipyardWaypoint}; the API sells a ship only where one is (D30).",
                estimatedCost);
        }

        var quote = await QuoteAsync(cached, shipType, cancellationToken);
        var quotedCost = quote?.PurchasePrice is > 0 and var quotedPrice ? quotedPrice : estimatedCost;

        // D97 (slice 6.28, asked on 2026-10-06): "It should also check whether the probes are in SCARCE supply and not buy them
        // if they are", read as every probe purchase. The probe plan leaves a shipyard whose cached supply is SCARCE alone; this
        // is the supply as the shipyard gives it now, which the last purchase there may have brought down. D112 (slice 6.33): the
        // same for the cargo ship bought once Trade.ShipPurchases is.
        var isProbe = shipType.Equals(ProbeShipType, StringComparison.OrdinalIgnoreCase);
        if ((isProbe || unlessScarce)
            && (quote?.Supply ?? string.Empty).Equals(ScarceSupply, StringComparison.OrdinalIgnoreCase))
        {
            return Failed(ShipPurchaseFailure.Scarce, $"{shipType} is {ScarceSupply} at {shipyardWaypoint} ({(isProbe ? "D97" : "D112")}).", quotedCost);
        }

        if (quotedCost != estimatedCost)
        {
            estimatedCost = quotedCost;
            decision = await budget.EvaluateAsync(quotedCost, cancellationToken);
            if (!decision.CanAfford)
            {
                return Failed(ShipPurchaseFailure.OverBudget, decision.Reason, quotedCost);
            }
        }

        var result = await port.PurchaseShipAsync(shipType, shipyardWaypoint, cancellationToken);
        calls.Answer(shipyardWaypoint);
        purchases.Bought(result.ShipSymbol, ToShipType(shipType), TimeProvider.System.GetUtcNow());

        await agents.SetAgentAsync(bus, result.Agent, cancellationToken);

        // With what startup sync caches of a ship (slice 6.31): its mounts, modules and engine, so a ship bought since the last
        // restart has its warp drive, its sensor array and its speed at once.
        var newShip = new ShipModel(
            result.ShipSymbol,
            result.ShipNav.SystemSymbol,
            result.ShipNav.WaypointSymbol,
            result.ShipNav.Status,
            result.ShipNav.FlightMode,
            result.ShipFuel.Current,
            result.ShipFuel.Capacity,
            result.ShipNav.ArrivesAt,
            result.ShipNav.DestWaypointSymbol,
            result.ShipCargo.Units,
            result.ShipCargo.Capacity,
            ShipType: shipType,
            MountSymbols: result.MountSymbols,
            CargoInventory: result.ShipCargo.Inventory,
            ModulesJson: result.ModulesJson,
            FrameJson: result.FrameJson,
            ReactorJson: result.ReactorJson,
            EngineJson: result.EngineJson);

        await ships.UpsertAsync(newShip, cancellationToken);

        // The name the bot gives it (slice 2.14, D72): the next number of its type. Told now, so its first lines carry it.
        var name = names.Know(await ships.GetAllAsync(cancellationToken)).GetValueOrDefault(result.ShipSymbol, string.Empty);

        // The ledger and the credits-spent metric (B7).
        await bus.PublishAsync(new NewShipPurchasedEvent(result.ShipSymbol, ToShipType(shipType), result.Cost));

        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} ({ShipType}) bought at {WaypointSymbol} for {Cost} credits; the bot calls it {ShipName}.",
            JournalEvents.ShipPurchased,
            result.ShipSymbol,
            shipType,
            shipyardWaypoint,
            result.Cost,
            name);

        return new ShipPurchaseResult
        {
            IsSuccess = true,
            EstimatedCost = estimatedCost,
            ActualCost = result.Cost,
            PurchasedShip = newShip,
        };
    }

    /// <summary>The API's <c>SHIP_MINING_DRONE</c> as <see cref="ShipType.ShipMiningDrone"/>; <see cref="ShipType.None"/> if unknown.</summary>
    internal static ShipType ToShipType(string shipType)
        => Enum.TryParse<ShipType>(shipType.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var type)
            ? type
            : ShipType.None;

    private static ShipPurchaseResult Failed(ShipPurchaseFailure failure, string? reason, long estimatedCost) => new()
    {
        IsSuccess = false,
        Failure = failure,
        FailureReason = reason,
        EstimatedCost = estimatedCost,
    };

    private static long ResolveShipPurchasePrice(ShipyardWaypointDto? dto, string shipType)
    {
        if (dto is null)
        {
            return 0;
        }

        var match = dto.Ships.FirstOrDefault(s => shipType.Equals(s.Type, StringComparison.OrdinalIgnoreCase));
        return match?.PurchasePrice ?? 0;
    }

    /// <summary>Whether one of our ships is at the waypoint, not in flight; arrivals are dead-reckoned.</summary>
    private async Task<bool> HasShipAtAsync(string waypointSymbol, CancellationToken cancellationToken)
        => (await ships.GetAllAsync(cancellationToken)).Any(ship =>
            ship.LocalStatus != ShipLocalStatus.InTransit
            && string.Equals(ship.WaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The ship as the shipyard lists it now, its price and supply, fetched while our ship is there, and stored for the other
    /// plans; as cached when the fetch fails or lists no ships.
    /// </summary>
    private async Task<ShipyardShipDto?> QuoteAsync(ShipyardWaypointDto cached, string shipType, CancellationToken cancellationToken)
    {
        try
        {
            var fresh = await port.GetShipyardAsync(cached.SystemSymbol, cached.WaypointSymbol, cancellationToken);
            if (fresh is null || string.IsNullOrWhiteSpace(fresh.ShipsDetailJson))
            {
                return ShipOf(cached, shipType);
            }

            await shipyards.UpsertAsync(fresh, cancellationToken);
            return ShipOf(await shipyards.FindByWaypointAsync(cached.WaypointSymbol, cancellationToken), shipType) ?? ShipOf(cached, shipType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                ex,
                "Ship purchase: couldn't fetch the shipyard at {WaypointSymbol} again; buying at its cached price {Cost}.",
                cached.WaypointSymbol,
                ResolveShipPurchasePrice(cached, shipType));
            return ShipOf(cached, shipType);
        }
    }

    private static ShipyardShipDto? ShipOf(ShipyardWaypointDto? shipyard, string shipType)
        => shipyard?.Ships.FirstOrDefault(ship => shipType.Equals(ship.Type, StringComparison.OrdinalIgnoreCase));
}
