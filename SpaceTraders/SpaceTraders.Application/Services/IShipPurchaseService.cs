using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

public interface IShipPurchaseService
{
    /// <summary>
    /// Buys the ship at the shipyard, within the credit reserve, where one of our ships is (D30). Nothing is bought where the
    /// shipyard has the ship SCARCE, by its supply as fetched just before the purchase (D121, which D97 and D112 began);
    /// <see cref="ShipPurchaseFailure.Scarce"/> then.
    /// </summary>
    /// <param name="shipType">The ship, such as <c>SHIP_LIGHT_HAULER</c>.</param>
    /// <param name="shipyardWaypoint">The shipyard.</param>
    /// <param name="cancellationToken">Stops the purchase.</param>
    /// <returns>What happened.</returns>
    Task<ShipPurchaseResult> TryPurchaseAsync(
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
    /// The ship's supply at the shipyard is SCARCE, as fetched just before the purchase: none is bought (D121; for the probes D97,
    /// for the largest hold D112, before it).
    /// </summary>
    Scarce = 5,
}
