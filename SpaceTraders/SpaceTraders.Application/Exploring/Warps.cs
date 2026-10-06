using System.Text.Json;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Exploring;

/// <summary>
/// How a warp works (PLAN.md slice 6.31, D100: "Research how the warp works exactly, then measure, then fuel-safe."), as read
/// on 2026-10-06 in the API's docs (a warp "behaves very similar to normal waypoint travel in that it takes time and consumes
/// normal fuel"), its OpenAPI spec 2.3.0 (<c>POST my/ships/{ship}/warp</c> to a waypoint of another system, from orbit, with a
/// warp drive installed) and the api-docs wiki's "Travel Fuel and Time", which the players compiled:
/// <list type="bullet">
///   <item>the distance is the straight one between the two systems' positions, rounded;</item>
///   <item>the fuel is a flight's (<see cref="FlightFuel"/>): the distance in CRUISE, twice that in BURN, 1 in DRIFT;</item>
///   <item>the time is round(round(distance) × multiplier / engine speed + 15) seconds, the multiplier 50 in CRUISE, twice a
///   flight's 25, and 25 in BURN, which the wiki marks as not confirmed since API 2.1;</item>
///   <item>the drive's range (2,000 for the explorer's Warp Drive I) caps the distance here: the API names no error for a warp
///   beyond it, and the wiki says the fuel limits a warp before the range does.</item>
/// </list>
/// Asked on 2026-10-06 (D104), "CRUISE/BURN only": no warp drifts. A warp reaches as far as the fuel aboard pays for, in BURN
/// where the fuel pays twice the distance and that strands nothing (as D84 flies), in CRUISE otherwise. Each warp's journal line
/// holds the fuel and the seconds it took against these (<c>Warped</c>), so the first one measures them.
/// </summary>
public static class Warps
{
    /// <summary>The start of a warp drive module's symbol: <c>MODULE_WARP_DRIVE_I</c>, <c>_II</c> or <c>_III</c>.</summary>
    public const string DriveModule = "MODULE_WARP_DRIVE";

    /// <summary>A warp's CRUISE multiplier: a unit of distance takes 50 seconds over the engine's speed, twice a flight's 25.</summary>
    public const double CruiseMultiplier = 50;

    /// <summary>A warp's BURN multiplier, half CRUISE's, as a flight's: not confirmed since API 2.1, so the first BURN warp measures it.</summary>
    public const double BurnMultiplier = 25;

    /// <summary>What the API adds to every warp, as to every flight.</summary>
    private const double SecondsPerWarp = 15;

    /// <summary>Whether the ship has a warp drive installed: its cached modules list one.</summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship that can warp.</returns>
    public static bool HasDrive(ShipModel ship) => Range(ship) > 0;

    /// <summary>
    /// The farthest the ship's warp drive warps: the range of its warp drive module, as cached; the most an int holds for a drive
    /// that gives none, as the fuel limits it then; 0 without a drive, or before the ship's modules are cached.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>The range, in units of distance between systems.</returns>
    public static int Range(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        if (string.IsNullOrWhiteSpace(ship.ModulesJson) || !ship.ModulesJson.Contains(DriveModule, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        try
        {
            using var modules = JsonDocument.Parse(ship.ModulesJson);
            if (modules.RootElement.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            var range = 0;
            for (var index = 0; index < modules.RootElement.GetArrayLength(); index++)
            {
                var module = modules.RootElement[index];
                if (module.ValueKind == JsonValueKind.Object
                    && (module.TryGetProperty("symbol", out var symbol) || module.TryGetProperty("Symbol", out symbol))
                    && symbol.ValueKind == JsonValueKind.String
                    && (symbol.GetString() ?? string.Empty).StartsWith(DriveModule, StringComparison.OrdinalIgnoreCase))
                {
                    var drive = int.MaxValue;
                    if ((module.TryGetProperty("range", out var reach) || module.TryGetProperty("Range", out reach))
                        && reach.ValueKind == JsonValueKind.Number
                        && reach.TryGetInt32(out var value)
                        && value > 0)
                    {
                        drive = value;
                    }

                    range = Math.Max(range, drive);
                }
            }

            return range;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>The straight distance between two systems' positions.</summary>
    /// <param name="from">Where one system lies.</param>
    /// <param name="to">Where the other lies.</param>
    /// <returns>The distance.</returns>
    public static double Distance((int X, int Y) from, (int X, int Y) to)
    {
        var dx = (double)to.X - from.X;
        var dy = (double)to.Y - from.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>The fuel a warp of <paramref name="distance"/> burns in <paramref name="flightMode"/>: a flight's (<see cref="FlightFuel"/>).</summary>
    /// <param name="flightMode">CRUISE or BURN.</param>
    /// <param name="distance">The distance between the two systems.</param>
    /// <returns>The fuel.</returns>
    public static int Fuel(string flightMode, double distance) => FlightFuel.Needed(flightMode, Math.Max(1, distance));

    /// <summary>The seconds a warp of <paramref name="distance"/> takes in <paramref name="flightMode"/> at <paramref name="speed"/>.</summary>
    /// <param name="flightMode">CRUISE or BURN; anything else counts as CRUISE.</param>
    /// <param name="distance">The distance between the two systems.</param>
    /// <param name="speed">The engine's speed.</param>
    /// <returns>The seconds, as the API reckons them.</returns>
    public static double Seconds(string flightMode, double distance, int speed)
    {
        var multiplier = string.Equals(flightMode, "BURN", StringComparison.OrdinalIgnoreCase) ? BurnMultiplier : CruiseMultiplier;
        var units = Math.Round(Math.Max(1, distance), MidpointRounding.AwayFromZero);
        return Math.Round((units * multiplier / Math.Max(1, speed)) + SecondsPerWarp, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The mode a warp flies in (D104): BURN where the fuel aboard pays for it and still leaves <paramref name="keep"/>, CRUISE
    /// where that does, and none where neither does: a warp never drifts.
    /// </summary>
    /// <param name="distance">The distance between the two systems.</param>
    /// <param name="fuel">The fuel aboard when the ship warps.</param>
    /// <param name="keep">The fuel the warp must leave: none where it lands where the ship can refuel, the way back otherwise.</param>
    /// <param name="flightMode">BURN or CRUISE.</param>
    /// <returns>False when the fuel pays for neither.</returns>
    public static bool TryChooseMode(double distance, int fuel, int keep, out string flightMode)
    {
        flightMode = "CRUISE";
        if (Fuel("BURN", distance) + keep <= fuel)
        {
            flightMode = "BURN";
            return true;
        }

        return Fuel("CRUISE", distance) + keep <= fuel;
    }
}
