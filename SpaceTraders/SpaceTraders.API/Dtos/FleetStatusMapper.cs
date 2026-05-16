using System.Text.Json;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;

namespace SpaceTraders.API.Dtos;

/// <summary>
/// Static mapper that converts fleet status domain read-models to their API DTO equivalents.
/// </summary>
/// <remarks>Phase 16b: response DTO mapping.</remarks>
public static class FleetStatusMapper
{
    /// <summary>Maps an <see cref="OrchestratorGoalChain"/> to a <see cref="FleetGoalChainDto"/>.</summary>
    public static FleetGoalChainDto ToDto(OrchestratorGoalChain chain) =>
        new()
        {
            FleetGoalId = chain.FleetGoalId,
            FleetGoalKind = chain.FleetGoalKind.ToString(),
            Priority = chain.Priority,
            FleetGoalDescription = chain.FleetGoalDescription,
            ResourceNeeds = chain.ResourceNeeds.Select(ToDto).ToList(),
        };

    /// <summary>Maps a <see cref="ResourceNeedEntry"/> to a <see cref="ResourceNeedEntryDto"/>.</summary>
    public static ResourceNeedEntryDto ToDto(ResourceNeedEntry entry) =>
        new()
        {
            TradeSymbol = entry.TradeSymbol,
            UnitsNeeded = entry.UnitsNeeded,
            UnitsDelivered = entry.UnitsDelivered,
            PurposeDescription = entry.PurposeDescription,
            AssignedShips = entry.AssignedShips,
        };

    /// <summary>Maps a <see cref="ShipAssignmentSnapshot"/> to a <see cref="ShipAssignmentDto"/>.</summary>
    public static ShipAssignmentDto ToDto(ShipAssignmentSnapshot snapshot) =>
        new()
        {
            ShipSymbol = snapshot.ShipSymbol,
            GoalKind = snapshot.GoalKind.ToString(),
            GoalDescription = snapshot.GoalDescription,
            SourceWaypoint = snapshot.SourceWaypoint,
            DestinationWaypoint = snapshot.DestinationWaypoint,
            FleetGoalId = snapshot.FleetGoalId,
            FleetGoalDescription = snapshot.FleetGoalDescription,
            AssignedAt = snapshot.AssignedAt,
        };

    /// <summary>Maps a <see cref="ShipActivitySnapshot"/> to a <see cref="ShipActivityDto"/>.</summary>
    public static ShipActivityDto ToDto(ShipActivitySnapshot snapshot) =>
        new()
        {
            ShipSymbol = snapshot.ShipSymbol,
            LocalStatus = snapshot.LocalStatus.ToString(),
            CurrentWaypoint = snapshot.CurrentWaypoint,
            DestinationWaypoint = snapshot.DestinationWaypoint,
            EstimatedArrival = snapshot.EstimatedArrival,
            OnCooldown = snapshot.OnCooldown,
            CooldownExpiresAt = snapshot.CooldownExpiresAt,
            CargoUsed = snapshot.CargoUsed,
            CargoCapacity = snapshot.CargoCapacity,
            FuelCurrent = snapshot.FuelCurrent,
            FuelCapacity = snapshot.FuelCapacity,
            ActivityDescription = snapshot.ActivityDescription,
        };

    /// <summary>Maps a <see cref="ShipGoalHistoryEntry"/> to a <see cref="ShipGoalHistoryDto"/>.</summary>
    public static ShipGoalHistoryDto ToDto(ShipGoalHistoryEntry entry) =>
        new()
        {
            Id = entry.Id,
            GoalKind = entry.GoalKind,
            Outcome = entry.Outcome,
            Reason = entry.Reason,
            StartedAt = entry.StartedAt,
            EndedAt = entry.EndedAt,
        };

    /// <summary>Maps a cached waypoint model to a detailed waypoint response DTO.</summary>
    public static WaypointDetailDto ToDto(WaypointCacheModel waypoint) =>
        new()
        {
            Symbol = waypoint.Symbol,
            SystemSymbol = waypoint.SystemSymbol,
            Type = waypoint.Type,
            X = waypoint.X,
            Y = waypoint.Y,
            HasMarket = waypoint.HasMarket,
            HasShipyard = waypoint.HasShipyard,
            IsUnderConstruction = waypoint.IsUnderConstruction,
            LastObservedAt = waypoint.LastObservedAt,
            ParentSymbol = waypoint.ParentSymbol,
            TraitsJson = waypoint.TraitsJson,
            ModifiersJson = waypoint.ModifiersJson,
            OrbitalsJson = waypoint.OrbitalsJson,
            ChartJson = waypoint.ChartJson,
            ExtractableResources = InferExtractableResources(waypoint),
        };

    private static IReadOnlyList<string> InferExtractableResources(WaypointCacheModel waypoint)
    {
        var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = ParseSymbolTokens(waypoint.TraitsJson)
            .Concat(ParseSymbolTokens(waypoint.ModifiersJson))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tokens.Contains("COMMON_METAL_DEPOSITS"))
        {
            resources.Add("IRON_ORE");
            resources.Add("COPPER_ORE");
            resources.Add("ALUMINUM_ORE");
        }

        if (tokens.Contains("PRECIOUS_METAL_DEPOSITS") || tokens.Contains("RARE_METAL_DEPOSITS"))
        {
            resources.Add("SILVER_ORE");
            resources.Add("GOLD_ORE");
            resources.Add("PLATINUM_ORE");
            resources.Add("MERITIUM_ORE");
        }

        if (tokens.Contains("PRECIOUS_STONE_DEPOSITS"))
        {
            resources.Add("DIAMONDS");
        }

        if (tokens.Contains("AMMONIA_ICE"))
        {
            resources.Add("AMMONIA_ICE");
        }

        if (tokens.Contains("ICE_CRYSTALS"))
        {
            resources.Add("ICE_WATER");
        }

        if (tokens.Contains("SILICON_CRYSTALS"))
        {
            resources.Add("SILICON_CRYSTALS");
        }

        if (tokens.Contains("QUARTZ_SAND"))
        {
            resources.Add("QUARTZ_SAND");
        }

        foreach (var token in tokens)
        {
            if (token.EndsWith("_ORE", StringComparison.OrdinalIgnoreCase)
                || token.EndsWith("_CRYSTALS", StringComparison.OrdinalIgnoreCase)
                || token.EndsWith("_ICE", StringComparison.OrdinalIgnoreCase)
                || token.EndsWith("_SAND", StringComparison.OrdinalIgnoreCase)
                || token.Equals("DIAMONDS", StringComparison.OrdinalIgnoreCase))
            {
                resources.Add(token.ToUpperInvariant());
            }
        }

        return resources.OrderBy(resource => resource, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ParseSymbolTokens(string? json)
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
            var length = document.RootElement.GetArrayLength();
            for (var index = 0; index < length; index++)
            {
                var element = document.RootElement[index];
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("symbol", out var symbol)
                    && symbol.ValueKind == JsonValueKind.String)
                {
                    var value = symbol.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        symbols.Add(value.ToUpperInvariant());
                    }
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        symbols.Add(value.ToUpperInvariant());
                    }
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
