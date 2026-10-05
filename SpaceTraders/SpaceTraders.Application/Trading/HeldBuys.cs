using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// The goods that trips on their way to buy hold at their buy markets: one buyer of a good at a market at a time (D80). Every
/// purchase raises the price, by about 2% to 6% a batch (<see cref="PriceSteps"/>), so a second trip sent there would find the
/// market the first left behind, not the one its estimate started from. On 2026-10-05 SPECTER-D and SPECTER-E were both sent
/// for EQUIPMENT at K94 at once, D bought first and E dropped its trip on arrival, seven times since the reset. Asked the same
/// day which way to go: "One buyer at a time".
/// </summary>
/// <remarks>
/// A trade trip or a construction trip (slice 6.6) holds its good at its buy market from the moment it starts until its cargo
/// is aboard, as it holds back its credits (<see cref="TripReservations.IsOnItsWayToBuy(TradeBetweenMarketsGoal)"/>, D57).
/// Two trips that have bought may still carry a good from one market, each to its own sell market (D18 keeps them off the
/// same route).
/// </remarks>
public sealed class HeldBuys
{
    private readonly HashSet<string> _held;

    private HeldBuys(HashSet<string> held) => _held = held;

    /// <summary>No trip on its way to buy.</summary>
    public static HeldBuys None { get; } = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>What the trips on their way to buy hold.</summary>
    /// <param name="tradeTrips">The fleet's trade trips; those that have bought, or are blocked or done, hold nothing.</param>
    /// <param name="constructionTrips">The fleet's construction trips, likewise.</param>
    /// <returns>The goods held at each market.</returns>
    public static HeldBuys Of(IEnumerable<TradeBetweenMarketsGoal> tradeTrips, IEnumerable<SupplyConstructionGoal> constructionTrips)
    {
        ArgumentNullException.ThrowIfNull(tradeTrips);
        ArgumentNullException.ThrowIfNull(constructionTrips);

        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trip in tradeTrips.Where(TripReservations.IsOnItsWayToBuy))
        {
            held.Add(Key(trip.TradeSymbol, trip.BuyWaypointSymbol));
        }

        foreach (var trip in constructionTrips.Where(TripReservations.IsOnItsWayToBuy))
        {
            held.Add(Key(trip.TradeSymbol, trip.BuyWaypointSymbol));
        }

        return new HeldBuys(held);
    }

    /// <summary>Whether a trip on its way to buy holds a good at a market, so no other trip is sent there for it.</summary>
    /// <param name="tradeSymbol">The good.</param>
    /// <param name="buyWaypointSymbol">The market.</param>
    /// <returns>True when another trip is on its way to buy it there.</returns>
    public bool Holds(string tradeSymbol, string buyWaypointSymbol) => _held.Contains(Key(tradeSymbol, buyWaypointSymbol));

    private static string Key(string tradeSymbol, string buyWaypointSymbol) => $"{buyWaypointSymbol}|{tradeSymbol}".ToUpperInvariant();
}
