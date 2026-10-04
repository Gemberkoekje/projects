using System.Globalization;
using System.Text.Json;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Naming;

/// <summary>
/// The names the bot gives its ships (slice 2.14, D72). The game names them after the agent, in the order they join the
/// fleet (SPECTER-1, SPECTER-2, …), which says nothing of what each is, and the API can't rename them. Asked on 2026-10-04:
/// "Can we make custom names within the API which should be type-number … Bonus points if there's a list of relevant names
/// for each of the types, one of which is picked per reset to call that type, e.g. all sattelites being called SPUTNIK-1,
/// SPUTNIK-2 etc."
/// <list type="bullet">
///   <item>each type a shipyard sells (the API's <c>ShipType</c>) has a list of names (<see cref="Lists"/>), and each server
///   reset picks one of them for the type, by its reset date;</item>
///   <item>the ships of a type are numbered after it in the order they joined the fleet, which is the order of the game's own
///   numbers: SPUTNIK-1, SPUTNIK-2;</item>
///   <item>a ship's type is the one it was bought as, else what its frame and mounts make it (<see cref="TypeOf"/>): a mining
///   drone and a siphon drone are both EXCAVATORs to the game, but not to this;</item>
///   <item>a ship whose type has no list is named after its registration role: COMMAND-1, EXCAVATOR-1.</item>
/// </list>
/// Without any I/O: a name follows from the fleet and the reset date alone, so a restart gives every ship the name it had,
/// and a ship joining the fleet takes the next number of its type.
/// </summary>
public static class ShipNames
{
    private const string ShipTypePrefix = "SHIP_";

    /// <summary>What a ship with no role is named after.</summary>
    private const string Unknown = "SHIP";

    /// <summary>The frames a type is told by: a drone's by its mount, a heavy freighter's by a refinery.</summary>
    private static readonly IReadOnlyDictionary<string, string> TypeByFrame = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["FRAME_PROBE"] = "SHIP_PROBE",
        ["FRAME_FRIGATE"] = "SHIP_COMMAND_FRIGATE",
        ["FRAME_SHUTTLE"] = "SHIP_LIGHT_SHUTTLE",
        ["FRAME_LIGHT_FREIGHTER"] = "SHIP_LIGHT_HAULER",
        ["FRAME_HEAVY_FREIGHTER"] = "SHIP_HEAVY_FREIGHTER",
        ["FRAME_BULK_FREIGHTER"] = "SHIP_BULK_FREIGHTER",
        ["FRAME_MINER"] = "SHIP_ORE_HOUND",
        ["FRAME_EXPLORER"] = "SHIP_EXPLORER",
        ["FRAME_INTERCEPTOR"] = "SHIP_INTERCEPTOR",
    };

    /// <summary>
    /// The names to pick from, for each type a shipyard sells (the API's <c>ShipType</c>). No name is in two lists, and none is
    /// a registration role, which names a ship of a type with no list.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Lists { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
        // Probes, after the probes and telescopes sent out to look.
        ["SHIP_PROBE"] = ["SPUTNIK", "VOYAGER", "PIONEER", "MARINER", "CASSINI", "GALILEO", "HUBBLE", "KEPLER", "ROSETTA", "HUYGENS"],

        // Mining drones, after what digs.
        ["SHIP_MINING_DRONE"] = ["PICKAXE", "DIGGER", "BORER", "MOLE", "GOPHER", "BADGER", "DRILL", "CHISEL", "AUGER", "SHOVEL"],

        // Siphon drones, after what sips.
        ["SHIP_SIPHON_DRONE"] = ["MOSQUITO", "HUMMINGBIRD", "BUTTERFLY", "SNORKEL", "BELLOWS", "STRAW", "PIPETTE", "ZEPHYR"],

        // Survey ships, after the instruments of the trade.
        ["SHIP_SURVEYOR"] = ["COMPASS", "SEXTANT", "ASTROLABE", "THEODOLITE", "PROSPECTOR", "DOWSER", "QUADRANT", "PLUMBLINE"],

        // The command frigate, after flagships.
        ["SHIP_COMMAND_FRIGATE"] = ["ENTERPRISE", "VICTORY", "ENDEAVOUR", "NAUTILUS", "DEFIANT", "INTREPID", "RESOLUTE", "VALIANT"],

        // Light shuttles, after small birds.
        ["SHIP_LIGHT_SHUTTLE"] = ["SPARROW", "ROBIN", "SWALLOW", "FINCH", "WREN", "LARK", "SWIFT", "STARLING"],

        // Light haulers, after pack animals.
        ["SHIP_LIGHT_HAULER"] = ["MULE", "PONY", "LLAMA", "CAMEL", "YAK", "BURRO", "ALPACA", "OX"],

        // Heavy freighters, after big animals.
        ["SHIP_HEAVY_FREIGHTER"] = ["ELEPHANT", "MAMMOTH", "RHINO", "HIPPO", "BISON", "MOOSE", "WALRUS", "BUFFALO"],

        // Bulk freighters, after giants.
        ["SHIP_BULK_FREIGHTER"] = ["LEVIATHAN", "BEHEMOTH", "COLOSSUS", "TITAN", "GOLIATH", "JUGGERNAUT", "ATLAS", "GARGANTUA"],

        // Refining freighters, after what melts ore.
        ["SHIP_REFINING_FREIGHTER"] = ["CRUCIBLE", "FORGE", "FURNACE", "SMELTER", "KILN", "CAULDRON", "ANVIL", "FOUNDRY"],

        // Ore hounds, after hounds.
        ["SHIP_ORE_HOUND"] = ["BEAGLE", "BLOODHOUND", "MASTIFF", "TERRIER", "DACHSHUND", "BASSET", "POINTER", "WOLFHOUND"],

        // Explorers, after explorers.
        ["SHIP_EXPLORER"] = ["MAGELLAN", "SHACKLETON", "AMUNDSEN", "VESPUCCI", "TASMAN", "DRAKE", "NANSEN", "BARENTS"],

        // Interceptors, after birds of prey.
        ["SHIP_INTERCEPTOR"] = ["FALCON", "HAWK", "OSPREY", "HARRIER", "MERLIN", "GOSHAWK", "PEREGRINE", "EAGLE"],
    };

    /// <summary>
    /// The names of a fleet's ships, by symbol: each type's name for the reset, numbered in the order its ships joined the
    /// fleet. Give it the whole fleet: a ship's number counts the ships of its type before it.
    /// </summary>
    /// <param name="fleet">Every ship of the agent.</param>
    /// <param name="resetDate">The server reset the agent was registered under, such as <c>2026-10-04</c>.</param>
    /// <returns>Each ship's name, by symbol.</returns>
    public static IReadOnlyDictionary<string, string> For(IEnumerable<ShipModel> fleet, string resetDate)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(resetDate);

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in fleet.GroupBy(ship => NameOfKind(ship, resetDate), StringComparer.Ordinal))
        {
            var number = 0;
            foreach (var ship in kind.OrderBy(JoinedAt).ThenBy(ship => ship.Symbol, StringComparer.Ordinal))
            {
                names[ship.Symbol] = $"{kind.Key}-{++number}";
            }
        }

        return names;
    }

    /// <summary>
    /// The type a ship is, as a shipyard sells it (the API's <c>ShipType</c>): the one it was bought as, while it is cached
    /// by that (until the next start's sync caches its registration role, B25); else what its frame makes it, a drone by its
    /// mount and a heavy freighter by a refinery. Empty when neither tells.
    /// </summary>
    /// <param name="ship">The ship, as cached.</param>
    /// <returns>Its type, such as <c>SHIP_MINING_DRONE</c>, or empty.</returns>
    public static string TypeOf(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);

        if (ship.ShipType.StartsWith(ShipTypePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ship.ShipType.ToUpperInvariant();
        }

        var frame = FrameOf(ship.FrameJson);
        if (frame.Equals("FRAME_DRONE", StringComparison.OrdinalIgnoreCase))
        {
            return Mounts(ship, "SURVEYOR") ? "SHIP_SURVEYOR"
                : Mounts(ship, "GAS_SIPHON") ? "SHIP_SIPHON_DRONE"
                : Mounts(ship, "MINING_LASER") ? "SHIP_MINING_DRONE"
                : string.Empty;
        }

        if (frame.Equals("FRAME_HEAVY_FREIGHTER", StringComparison.OrdinalIgnoreCase)
            && (ship.ModulesJson ?? string.Empty).Contains("MODULE_ORE_REFINERY", StringComparison.OrdinalIgnoreCase))
        {
            return "SHIP_REFINING_FREIGHTER";
        }

        return TypeByFrame.GetValueOrDefault(frame, string.Empty);
    }

    /// <summary>
    /// What a ship's name is for: its type as a shipyard sells it (<see cref="TypeOf"/>), else its registration role as
    /// cached, such as <c>PATROL</c>; <c>SHIP</c> without either.
    /// </summary>
    /// <param name="ship">The ship, as cached.</param>
    /// <returns>The type or role, in upper case.</returns>
    public static string KindOf(ShipModel ship)
    {
        var type = TypeOf(ship);
        if (type.Length > 0)
        {
            return type;
        }

        return string.IsNullOrWhiteSpace(ship.ShipType) ? Unknown : ship.ShipType.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// What a ship's name is made of before its number: the name its type goes by this reset; for a type with no list, the
    /// type without its <c>SHIP_</c>, or the registration role, as cached.
    /// </summary>
    private static string NameOfKind(ShipModel ship, string resetDate)
    {
        var kind = KindOf(ship);
        if (Lists.TryGetValue(kind, out var names))
        {
            return names[Pick(resetDate, kind, names.Count)];
        }

        return kind.StartsWith(ShipTypePrefix, StringComparison.Ordinal) && kind.Length > ShipTypePrefix.Length
            ? kind[ShipTypePrefix.Length..]
            : kind;
    }

    /// <summary>
    /// Which name of a list a reset gives a type: the same on every start of the reset, and on every machine (FNV-1a, not
    /// <see cref="string.GetHashCode()"/>, which changes with every process).
    /// </summary>
    private static int Pick(string resetDate, string type, int count)
    {
        var hash = 2_166_136_261u;
        foreach (var character in $"{resetDate}|{type}")
        {
            hash = unchecked((hash ^ character) * 16_777_619u);
        }

        return (int)(hash % (uint)count);
    }

    /// <summary>
    /// When a ship joined the fleet: the game numbers an agent's ships in that order, in hexadecimal, after its symbol
    /// (SPECTER-F joined before SPECTER-10). A symbol without such a number comes last.
    /// </summary>
    private static long JoinedAt(ShipModel ship)
    {
        var dash = ship.Symbol.LastIndexOf('-');
        return dash >= 0 && long.TryParse(ship.Symbol.AsSpan(dash + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number)
            ? number
            : long.MaxValue;
    }

    private static bool Mounts(ShipModel ship, string kind)
        => (ship.MountSymbols ?? []).Any(mount => mount.Contains(kind, StringComparison.OrdinalIgnoreCase));

    /// <summary>The frame's symbol in a cached frame (<c>{"symbol":"FRAME_DRONE",…}</c>); empty without one.</summary>
    private static string FrameOf(string? frameJson)
    {
        if (string.IsNullOrWhiteSpace(frameJson))
        {
            return string.Empty;
        }

        try
        {
            using var frame = JsonDocument.Parse(frameJson);
            return frame.RootElement.ValueKind == JsonValueKind.Object
                && frame.RootElement.TryGetProperty("symbol", out var symbol)
                && symbol.ValueKind == JsonValueKind.String
                    ? symbol.GetString() ?? string.Empty
                    : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
