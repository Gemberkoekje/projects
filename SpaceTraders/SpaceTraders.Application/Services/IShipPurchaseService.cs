using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

public interface IShipPurchaseService
{
    Task<ShipPurchaseResult> TryPurchaseAsync(
        string shipType,
        string shipyardWaypoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="TryPurchaseAsync"/>, but buys nothing where the shipyard has the ship SCARCE, by its supply as fetched just
    /// before the purchase (slice 6.33, D112: the cargo ship bought once <c>Trade.ShipPurchases</c> is, "as long as it is not
    /// scarce"); <see cref="ShipPurchaseFailure.Scarce"/> then.
    /// </summary>
    /// <param name="shipType">The ship, such as <c>SHIP_LIGHT_HAULER</c>.</param>
    /// <param name="shipyardWaypoint">The shipyard.</param>
    /// <param name="cancellationToken">Stops the purchase.</param>
    /// <returns>What happened.</returns>
    Task<ShipPurchaseResult> TryPurchaseUnlessScarceAsync(
        string shipType,
        string shipyardWaypoint,
        CancellationToken cancellationToken = default);
}

public sealed record ShipPurchaseResult
{
    public bool IsSuccess { get; init; }

    /// <summary>Why the purchase didn't happen; <see cref="ShipPurchaseFailure.None"/> after a purchase.</summary>
    public ShipPurchaseFailure Failure { get; init; }

    public string? FailureReason { get; init; }

    public long EstimatedCost { get; init; }

    public ShipModel? PurchasedShip { get; init; }

    public long ActualCost { get; init; }
}

/// <summary>Why <see cref="IShipPurchaseService.TryPurchaseAsync"/> bought nothing.</summary>
public enum ShipPurchaseFailure
{
    /// <summary>It bought the ship, or failed for a reason not listed here.</summary>
    None = 0,

    /// <summary>The ship type or the shipyard was empty.</summary>
    InvalidRequest = 1,

    /// <summary>The shipyard's price for the ship isn't cached.</summary>
    PriceUnknown = 2,

    /// <summary>The purchase would leave less than the credit reserve.</summary>
    OverBudget = 3,

    /// <summary>None of our ships is at the shipyard, which the API requires; a ship is called there (D30).</summary>
    NoShipAtShipyard = 4,

    /// <summary>
    /// A probe (D97), or a cargo ship bought once <c>Trade.ShipPurchases</c> is (D112), whose supply at the shipyard is SCARCE, as
    /// fetched just before the purchase: none is bought.
    /// </summary>
    Scarce = 5,
}
