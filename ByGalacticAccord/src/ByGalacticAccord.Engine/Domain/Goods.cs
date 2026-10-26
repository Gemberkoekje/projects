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

/// <summary>
/// Static, designer-tuned facts about a good. Prices move; these do not. A perishable good
/// <see cref="KeepsHours"/> in a standard hold (design §15.3): the time it takes to fall from fresh to the
/// standard warranty, which sets how fast it decays.
/// </summary>
public sealed record GoodInfo(
    GoodType Type,
    GoodCategory Category,
    decimal BaseValue,
    long? KeepsHours,
    bool Refrigeratable)
{
    public bool Perishable => KeepsHours is not null;

    /// <summary>Quality lost per hour in a standard hold: from fresh (1.0) to the standard warranty in <see cref="KeepsHours"/>.</summary>
    public decimal QualityDecayPerTick => KeepsHours is { } hours ? (1m - Goods.StandardWarranty) / hours : 0m;
}

/// <summary>Canonical good table. Balance values here are first-pass and meant to be tuned via soak runs (§5.6).</summary>
public static class Goods
{
    /// <summary>The quality a standard warranty asks for (design §15.3: "keeps" is the time until quality drops below it).</summary>
    public const decimal StandardWarranty = 0.7m;

    private static readonly Dictionary<GoodType, GoodInfo> Table = new()
    {
        // Design §15.3: food keeps about 7 days in a standard hold and 35 refrigerated, medicine 2 and 10.
        // A refrigeration unit slows decay to a fifth (design §15.4), so the refrigerated times follow.
        [GoodType.Food] = new(GoodType.Food, GoodCategory.Staple, BaseValue: 10m, KeepsHours: 7 * 24, Refrigeratable: true),
        [GoodType.Ore] = new(GoodType.Ore, GoodCategory.Raw, BaseValue: 8m, KeepsHours: null, Refrigeratable: false),
        [GoodType.Fuel] = new(GoodType.Fuel, GoodCategory.Raw, BaseValue: 14m, KeepsHours: null, Refrigeratable: false),
        [GoodType.Parts] = new(GoodType.Parts, GoodCategory.Industrial, BaseValue: 26m, KeepsHours: null, Refrigeratable: false),
        [GoodType.Machinery] = new(GoodType.Machinery, GoodCategory.Industrial, BaseValue: 55m, KeepsHours: null, Refrigeratable: false),
        [GoodType.Medicine] = new(GoodType.Medicine, GoodCategory.HighValue, BaseValue: 70m, KeepsHours: 2 * 24, Refrigeratable: true),
        [GoodType.Luxury] = new(GoodType.Luxury, GoodCategory.HighValue, BaseValue: 120m, KeepsHours: null, Refrigeratable: false),
    };

    public static GoodInfo Info(GoodType type) => Table[type];

    public static IReadOnlyCollection<GoodType> All { get; } = new[]
    {
        GoodType.Food, GoodType.Ore, GoodType.Fuel, GoodType.Parts,
        GoodType.Machinery, GoodType.Medicine, GoodType.Luxury,
    };
}
