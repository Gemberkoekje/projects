using System.Collections.Generic;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>The seven slice-0 goods. No exotics (§6.1).</summary>
public enum GoodType
{
    Food,
    Ore,
    Fuel,
    Parts,
    Machinery,
    Medicine,
    Luxury,
}

/// <summary>
/// Broad good categories. Reputation is keyed partly by good-category (§4.3): a pilot
/// trusted to haul staples may be a stranger to the luxury cartels.
/// </summary>
public enum GoodCategory
{
    Staple,
    Raw,
    Industrial,
    HighValue,
}

/// <summary>Static, designer-tuned facts about a good. Prices move; these do not.</summary>
public sealed record GoodInfo(
    GoodType Type,
    GoodCategory Category,
    decimal BaseValue,
    bool Perishable,
    decimal QualityDecayPerTick,
    bool Refrigeratable);

/// <summary>Canonical good table. Balance values here are first-pass and meant to be tuned via soak runs (§5.6).</summary>
public static class Goods
{
    private static readonly Dictionary<GoodType, GoodInfo> Table = new()
    {
        [GoodType.Food] = new(GoodType.Food, GoodCategory.Staple, BaseValue: 10m, Perishable: true, QualityDecayPerTick: 0.012m, Refrigeratable: true),
        [GoodType.Ore] = new(GoodType.Ore, GoodCategory.Raw, BaseValue: 8m, Perishable: false, QualityDecayPerTick: 0m, Refrigeratable: false),
        [GoodType.Fuel] = new(GoodType.Fuel, GoodCategory.Raw, BaseValue: 14m, Perishable: false, QualityDecayPerTick: 0m, Refrigeratable: false),
        [GoodType.Parts] = new(GoodType.Parts, GoodCategory.Industrial, BaseValue: 26m, Perishable: false, QualityDecayPerTick: 0m, Refrigeratable: false),
        [GoodType.Machinery] = new(GoodType.Machinery, GoodCategory.Industrial, BaseValue: 55m, Perishable: false, QualityDecayPerTick: 0m, Refrigeratable: false),
        [GoodType.Medicine] = new(GoodType.Medicine, GoodCategory.HighValue, BaseValue: 70m, Perishable: true, QualityDecayPerTick: 0.018m, Refrigeratable: true),
        [GoodType.Luxury] = new(GoodType.Luxury, GoodCategory.HighValue, BaseValue: 120m, Perishable: false, QualityDecayPerTick: 0m, Refrigeratable: false),
    };

    public static GoodInfo Info(GoodType type) => Table[type];

    public static IReadOnlyCollection<GoodType> All { get; } = new[]
    {
        GoodType.Food, GoodType.Ore, GoodType.Fuel, GoodType.Parts,
        GoodType.Machinery, GoodType.Medicine, GoodType.Luxury,
    };
}
