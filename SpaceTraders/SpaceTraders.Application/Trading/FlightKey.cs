namespace SpaceTraders.Application.Trading;

/// <summary>
/// A flight a map has planned (<see cref="TradeRoutePlanner.TryPlanFlight(TradeMarketMap, string, string, int, int, out TradeFlight)"/>):
/// where it starts and ends, the fuel aboard and the tank. Waypoint symbols compare ignoring case, as the maps' do. A value,
/// so looking a flight up builds no string (B88: about a million lookups a map).
/// </summary>
/// <param name="From">Where the flight starts.</param>
/// <param name="To">Where it ends.</param>
/// <param name="FuelAtStart">The fuel aboard on leaving.</param>
/// <param name="FuelCapacity">What the tank holds.</param>
internal readonly record struct FlightKey(string From, string To, int FuelAtStart, int FuelCapacity)
{
    /// <summary>Compares the waypoints ignoring case, and the fuel exactly.</summary>
    public static IEqualityComparer<FlightKey> Comparer { get; } = new IgnoringCase();

    private sealed class IgnoringCase : IEqualityComparer<FlightKey>
    {
        public bool Equals(FlightKey x, FlightKey y)
            => x.FuelAtStart == y.FuelAtStart
                && x.FuelCapacity == y.FuelCapacity
                && StringComparer.OrdinalIgnoreCase.Equals(x.From, y.From)
                && StringComparer.OrdinalIgnoreCase.Equals(x.To, y.To);

        public int GetHashCode(FlightKey obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.From),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.To),
                obj.FuelAtStart,
                obj.FuelCapacity);
    }
}
