using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// Cargo a free ship holds that nothing will sell or use (D42), asked on 2026-10-02: "if a ship's cargo hold isn't empty
/// and the goods aren't going to be sold or earmarked for another reason, the ship should either go to a waypoint to
/// sell it or, if that's not profitable, jettison it." Without any I/O, so the plans and their tests decide alike:
/// the good that fetches most after the fuel to get there is sold first, one good a trip, while that pays
/// (<see cref="TradeRoutePlanner.TryFindBestCargoSale"/>); once nothing aboard pays, every good that isn't earmarked
/// goes overboard.
/// </summary>
public static class HeldCargo
{
    /// <summary>No market the ship can reach buys the good.</summary>
    public const string NoBuyer = "no_buyer";

    /// <summary>The best sale doesn't pay for the fuel to get there.</summary>
    public const string NotWorthTheFuel = "not_worth_the_fuel";

    /// <summary>
    /// The goods to jettison: none while a good aboard pays for its sale, which goes first; then every good that isn't
    /// earmarked, with why it isn't worth keeping.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now, with its hold.</param>
    /// <param name="isEarmarked">Whether a good is kept for another reason: contract ore the ship will deliver, say.</param>
    /// <returns>The goods to throw overboard, each with its reason (<see cref="NoBuyer"/> or <see cref="NotWorthTheFuel"/>).</returns>
    public static IReadOnlyList<(CargoItemModel Cargo, string Reason)> ToJettison(TradeMarketMap map, ShipModel ship, Func<string, bool> isEarmarked)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(isEarmarked);

        if (TradeRoutePlanner.TryFindBestCargoSale(map, ship, mustSell: false, out _, out _))
        {
            return [];
        }

        return [.. (ship.CargoInventory ?? [])
            .Where(item => item.Units > 0 && !isEarmarked(item.Symbol))
            .OrderBy(item => item.Symbol, StringComparer.Ordinal)
            .Select(item => (item, TradeRoutePlanner.TryFindBestSale(map, ship, item.Symbol, item.Units, out _) ? NotWorthTheFuel : NoBuyer))];
    }
}
