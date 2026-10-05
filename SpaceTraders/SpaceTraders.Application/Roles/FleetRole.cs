using System.Text.Json.Serialization;

namespace SpaceTraders.Application.Roles;

/// <summary>
/// The work a ship does for the fleet (PLAN.md slice 6.9, D38): the role board gives every ship one, from what it
/// carries and what the others can do. Stored by name, so the plan state reads as it is meant.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FleetRole>))]
public enum FleetRole
{
    /// <summary>No role: a probe, which the probe plan flies, or a ship none of whose roles has a plan on.</summary>
    None = 0,

    /// <summary>Surveys for the miners (a surveyor mount), first of all (D38); in its spare time it trades or gathers (D34).</summary>
    Survey = 1,

    /// <summary>Mines (a mining laser, a hold and a tank): the contract first (D40), then the mining plan's trips.</summary>
    Mine = 2,

    /// <summary>Siphons gases (a gas siphon, a hold and a tank): the siphon plan's trips.</summary>
    Siphon = 3,

    /// <summary>Trades between markets (a hold and a tank): the trading plan's trips.</summary>
    Trade = 4,

    /// <summary>
    /// Builds the jump gate (a hold and a tank, and no drone): the construction plan's trips (slice 6.6, D65). It trades
    /// when the construction plan has nothing it may buy.
    /// </summary>
    Construct = 5,

    /// <summary>
    /// Collects the ore of drones parked at a far asteroid (a hold and a tank, and no drone): the mining plan's collecting
    /// rounds (slice 6.18, D83), for the shuttles the mining plan designated for that asteroid, once a drone is parked there
    /// (D86). It does nothing else.
    /// </summary>
    Collect = 6,
}
