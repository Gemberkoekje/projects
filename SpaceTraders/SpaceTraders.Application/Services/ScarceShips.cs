using SpaceTraders.Application.DTOs;

namespace SpaceTraders.Application.Services;

/// <summary>
/// No ship is bought where the shipyard has it SCARCE (PLAN.md D121). Asked on 2026-10-09, "As with the other ships, do not buy
/// INTERCEPTORS if the supply is SCARCE", then "Every purchase": until then only the probes (D97) and the largest hold beyond
/// <c>Trade.ShipPurchases</c> (D112) kept to it. Each plan leaves a shipyard whose cached listing says SCARCE alone when it
/// chooses where to buy, so a need that can't be met holds nothing after it in the order; the purchase refuses one the
/// shipyard, fetched again just before, lists at SCARCE (<see cref="IShipPurchaseService"/>).
/// </summary>
public static class ScarceShips
{
    /// <summary>The supply at which no ship is bought.</summary>
    public const string Supply = "SCARCE";

    /// <summary>Whether a supply is SCARCE.</summary>
    /// <param name="supply">The supply as a shipyard lists it; empty or null when not listed.</param>
    /// <returns>True for SCARCE.</returns>
    public static bool IsScarce(string? supply) => Supply.Equals(supply, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the shipyard lists the ship at SCARCE supply.</summary>
    /// <param name="ship">The ship as the shipyard lists it.</param>
    /// <returns>True when no plan may buy it there.</returns>
    public static bool IsScarce(ShipyardShipDto ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return IsScarce(ship.Supply);
    }
}
