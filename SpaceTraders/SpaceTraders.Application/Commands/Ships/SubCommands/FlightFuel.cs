namespace SpaceTraders.Application.Commands.Ships.SubCommands;

/// <summary>
/// The fuel a flight burns, as the API charges it (B62): the distance rounded, at least 1, in CRUISE and STEALTH, twice
/// that in BURN, and 1 in DRIFT. A flight between two waypoints at the same spot (a gas giant and its station) burns none
/// here, so the guard never holds back a flight that costs nothing. CRUISE is the route planner's own reckoning
/// (<c>TradeRoutePlanner</c>).
/// </summary>
internal static class FlightFuel
{
    /// <summary>The fuel a flight of <paramref name="distance"/> burns in <paramref name="flightMode"/>.</summary>
    /// <param name="flightMode">CRUISE, BURN, STEALTH or DRIFT; anything else counts as CRUISE.</param>
    /// <param name="distance">The straight distance between the two waypoints.</param>
    /// <returns>The fuel, 0 for no distance.</returns>
    public static int Needed(string? flightMode, double distance)
    {
        if (distance <= 0)
        {
            return 0;
        }

        var cruise = Math.Max(1, (int)Math.Round(distance, MidpointRounding.AwayFromZero));
        return flightMode?.ToUpperInvariant() switch
        {
            "DRIFT" => 1,
            "BURN" => 2 * cruise,
            _ => cruise,
        };
    }
}
