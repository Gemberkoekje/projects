using System.Collections.Generic;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// The goods (design §15.3): the seven ordinary goods, then the ship parts (design §6.1, §15.4), each kind at grade A, B
/// and C. A part's grade is part of what it is, so parts of different grades never merge in a hold, a store or a stock;
/// <see cref="ShipPart"/> maps a part good to its kind and grade and back. ("Parts", the fourth good, is Meridian's
/// industrial parts, an input; ship parts are the frames, drives and modules made from such inputs.)
/// </summary>
public enum GoodType
{
    Food,
    Ore,
    Fuel,
    Parts,
    Machinery,
    Medicine,
    Luxury,
    LightFrameA,
    LightFrameB,
    LightFrameC,
    BulkFrameA,
    BulkFrameB,
    BulkFrameC,
    CourierFrameA,
    CourierFrameB,
    CourierFrameC,
    StandardDriveA,
    StandardDriveB,
    StandardDriveC,
    FastDriveA,
    FastDriveB,
    FastDriveC,
    RefrigerationUnitA,
    RefrigerationUnitB,
    RefrigerationUnitC,
    ArmourPlatingA,
    ArmourPlatingB,
    ArmourPlatingC,
    CargoPodA,
    CargoPodB,
    CargoPodC,
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

    /// <summary>Frames, drives and modules: what ships are built from (design §6.1).</summary>
    ShipPart,
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

    private static readonly Dictionary<GoodType, GoodInfo> Table = Build();

    private static Dictionary<GoodType, GoodInfo> Build()
    {
        var table = new Dictionary<GoodType, GoodInfo>
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

        // A ship part's base value is its base price (design §15.4) at its grade's share of value (ShipPart.ValueShare).
        foreach (GoodType good in ShipPart.Goods)
        {
            (PartKind kind, Grade grade) = ShipPart.Of(good);
            table[good] = new GoodInfo(good, GoodCategory.ShipPart, ShipPart.Info(kind).BasePrice.Amount * ShipPart.ValueShare(grade), null, false);
        }

        return table;
    }

    public static GoodInfo Info(GoodType type) => Table[type];

    /// <summary>The seven ordinary goods (design §15.3), in their order: what consumers use and producers make.</summary>
    public static IReadOnlyList<GoodType> Trade { get; } = new[]
    {
        GoodType.Food, GoodType.Ore, GoodType.Fuel, GoodType.Parts,
        GoodType.Machinery, GoodType.Medicine, GoodType.Luxury,
    };

    /// <summary>Every good, the ordinary goods first, then the ship parts by kind and grade.</summary>
    public static IReadOnlyList<GoodType> All { get; } = System.Enum.GetValues<GoodType>();
}
