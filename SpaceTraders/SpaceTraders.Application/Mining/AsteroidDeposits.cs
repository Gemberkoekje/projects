using System.Text.Json;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Mining;

/// <summary>
/// What a waypoint can be mined for, from its type and traits (PLAN.md slice 6.4, B34). The game
/// doesn't publish the table; this is the one community bots use: each deposit trait's description
/// names some ores, and extractions have shown the rest. It matches what the bot mined at X1-DC53-XB5C
/// (COMMON_METAL_DEPOSITS) on 2026-10-02: aluminum, copper and iron ore, ice water, quartz sand and
/// silicon crystals, in about equal parts. A survey of a waypoint shows what is really there.
/// </summary>
public static class AsteroidDeposits
{
    /// <summary>Everything a mining laser can extract. Other goods (gases, refined goods) are never mined.</summary>
    public static readonly IReadOnlySet<string> Ores = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ALUMINUM_ORE",
        "AMMONIA_ICE",
        "COPPER_ORE",
        "GOLD_ORE",
        "ICE_WATER",
        "IRON_ORE",
        "MERITIUM_ORE",
        "PLATINUM_ORE",
        "PRECIOUS_STONES",
        "QUARTZ_SAND",
        "SILICON_CRYSTALS",
        "SILVER_ORE",
        "URANITE_ORE",
    };

    private static readonly IReadOnlySet<string> ExtractableTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ASTEROID",
        "ASTEROID_FIELD",
        "ENGINEERED_ASTEROID",
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> OresByTrait = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["COMMON_METAL_DEPOSITS"] = ["ALUMINUM_ORE", "COPPER_ORE", "IRON_ORE", "ICE_WATER", "SILICON_CRYSTALS", "QUARTZ_SAND"],
        ["MINERAL_DEPOSITS"] = ["SILICON_CRYSTALS", "QUARTZ_SAND", "AMMONIA_ICE", "ICE_WATER", "IRON_ORE", "PRECIOUS_STONES"],
        ["PRECIOUS_METAL_DEPOSITS"] = ["PLATINUM_ORE", "GOLD_ORE", "SILVER_ORE", "ALUMINUM_ORE", "COPPER_ORE", "ICE_WATER", "QUARTZ_SAND", "SILICON_CRYSTALS"],
        ["RARE_METAL_DEPOSITS"] = ["URANITE_ORE", "MERITIUM_ORE"],
        ["FROZEN"] = ["ICE_WATER", "AMMONIA_ICE"],
        ["ICE_CRYSTALS"] = ["ICE_WATER", "AMMONIA_ICE"],
    };

    /// <summary>Whether ships can extract at the waypoint: an asteroid, an asteroid field or an engineered asteroid.</summary>
    /// <param name="waypointType">The waypoint's type, as the API names it.</param>
    /// <returns>True for a waypoint a mining laser works at.</returns>
    public static bool IsExtractable(string waypointType)
        => ExtractableTypes.Contains(waypointType);

    /// <summary>The ores a waypoint can yield: none for one that can't be mined, or whose traits aren't known.</summary>
    /// <param name="waypoint">The waypoint, with its traits.</param>
    /// <returns>The ores, in the order of the table.</returns>
    public static IReadOnlyList<string> OresAt(WaypointCacheModel waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);

        if (!IsExtractable(waypoint.Type))
        {
            return [];
        }

        return [.. TraitSymbols(waypoint.TraitsJson)
            .SelectMany(trait => OresByTrait.TryGetValue(trait, out var ores) ? ores : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Whether a waypoint can yield an ore.</summary>
    /// <param name="waypoint">The waypoint, with its traits.</param>
    /// <param name="ore">The ore.</param>
    /// <returns>True when the waypoint can be mined and one of its traits yields the ore.</returns>
    public static bool CanYield(WaypointCacheModel waypoint, string ore)
        => OresAt(waypoint).Contains(ore, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The symbols in a cached traits or modifiers list, as startup sync and the API adapter store them
    /// (<c>[{"symbol":"COMMON_METAL_DEPOSITS"}]</c>).
    /// </summary>
    /// <param name="json">The cached list; null or invalid reads as none.</param>
    /// <returns>The symbols.</returns>
    public static IReadOnlyList<string> TraitSymbols(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var symbols = new List<string>();
            for (var index = 0; index < document.RootElement.GetArrayLength(); index++)
            {
                var item = document.RootElement[index];
                if (item.ValueKind == JsonValueKind.Object
                    && (item.TryGetProperty("symbol", out var symbol) || item.TryGetProperty("Symbol", out symbol))
                    && symbol.ValueKind == JsonValueKind.String)
                {
                    symbols.Add(symbol.GetString() ?? string.Empty);
                }
            }

            return symbols;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
