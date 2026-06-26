using System.Collections.Generic;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>The five slice-0 hulls. Each is a niche, not a rung on a strict power ladder (§3.6).</summary>
public enum ShipType
{
    LightFreighter,
    BulkHauler,
    FastCourier,
    RefrigeratedTransport,
    ArmedFreighter,
}

/// <summary>
/// Canonical (no-variance, §6.1) stats for a hull class. The "special" stats finally
/// mean something in the slice because the Transit Incident table reads them (§6.3).
/// </summary>
public sealed record ShipClassInfo(
    ShipType Type,
    string Name,
    decimal CargoCapacity,
    decimal SpeedMultiplier,
    Credits RunningCostPerTick,
    Credits MarketValue,
    decimal IncidentChanceModifier,
    decimal IncidentSeverityModifier,
    decimal Preservation,
    bool RunsHot)
{
    /// <summary>Niche description surfaced in the acquisition UI.</summary>
    public string Niche => Type switch
    {
        ShipType.LightFreighter => "Jack-of-all-trades starter hull. Runs hot above 80% speed: push to beat a deadline, raise incident odds.",
        ShipType.BulkHauler => "Cavernous and slow. Unlocks the ore and bulk contracts the frontier mining colonies post.",
        ShipType.FastCourier => "Quick but fragile. Beats deadlines; its thin hull worsens damage when an incident lands.",
        ShipType.RefrigeratedTransport => "Preserves perishable quality in transit. Chases the fat medical/food margins.",
        ShipType.ArmedFreighter => "Hardened against interdiction; shrugs off incident severity. Survives the dangerous runs.",
        _ => string.Empty,
    };
}

/// <summary>Canonical hull table. First-pass values; tuned via soak runs (§5.6).</summary>
public static class ShipClasses
{
    private static readonly Dictionary<ShipType, ShipClassInfo> Table = new()
    {
        [ShipType.LightFreighter] = new(
            ShipType.LightFreighter, "Light Freighter",
            CargoCapacity: 120m, SpeedMultiplier: 1.0m,
            RunningCostPerTick: Credits.Of(0.18m), MarketValue: Credits.Of(8_000m),
            IncidentChanceModifier: 1.0m, IncidentSeverityModifier: 1.0m, Preservation: 0m, RunsHot: true),

        [ShipType.BulkHauler] = new(
            ShipType.BulkHauler, "Bulk Hauler",
            CargoCapacity: 600m, SpeedMultiplier: 0.6m,
            RunningCostPerTick: Credits.Of(0.45m), MarketValue: Credits.Of(26_000m),
            IncidentChanceModifier: 1.1m, IncidentSeverityModifier: 1.15m, Preservation: 0m, RunsHot: false),

        [ShipType.FastCourier] = new(
            ShipType.FastCourier, "Fast Courier",
            CargoCapacity: 45m, SpeedMultiplier: 1.8m,
            RunningCostPerTick: Credits.Of(0.35m), MarketValue: Credits.Of(12_000m),
            IncidentChanceModifier: 0.85m, IncidentSeverityModifier: 1.4m, Preservation: 0m, RunsHot: false),

        [ShipType.RefrigeratedTransport] = new(
            ShipType.RefrigeratedTransport, "Refrigerated Transport",
            CargoCapacity: 140m, SpeedMultiplier: 0.9m,
            RunningCostPerTick: Credits.Of(0.4m), MarketValue: Credits.Of(18_000m),
            IncidentChanceModifier: 1.0m, IncidentSeverityModifier: 1.0m, Preservation: 0.85m, RunsHot: false),

        [ShipType.ArmedFreighter] = new(
            ShipType.ArmedFreighter, "Armed Freighter",
            CargoCapacity: 100m, SpeedMultiplier: 1.0m,
            RunningCostPerTick: Credits.Of(0.6m), MarketValue: Credits.Of(34_000m),
            IncidentChanceModifier: 0.6m, IncidentSeverityModifier: 0.5m, Preservation: 0m, RunsHot: false),
    };

    public static ShipClassInfo Info(ShipType type) => Table[type];

    public static IReadOnlyCollection<ShipType> All { get; } = new[]
    {
        ShipType.LightFreighter, ShipType.BulkHauler, ShipType.FastCourier,
        ShipType.RefrigeratedTransport, ShipType.ArmedFreighter,
    };
}
