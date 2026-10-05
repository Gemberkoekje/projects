using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>
/// The mining fixture's system (<see cref="MiningFixture"/>) with the jump gate's production chains, for slice 6.25 (D92). The
/// gate needs FAB_MATS, made from IRON and QUARTZ_SAND, and ADVANCED_CIRCUITRY, made from ELECTRONICS and MICROPROCESSORS,
/// both made from SILICON_CRYSTALS and COPPER, as the game's chains have them:
/// <list type="bullet">
///   <item>H51 smelts IRON from IRON_ORE, as H60 did in X1-FJ91 on 2026-10-05;</item>
///   <item>F49 smelts COPPER from COPPER_ORE, and ALUMINUM from ALUMINUM_ORE, which no gate material is made from;</item>
///   <item>H52, which sells the drones, makes FAB_MATS from IRON and QUARTZ_SAND: a factory, no smelter;</item>
///   <item>B7, far out, makes ELECTRONICS from SILICON_CRYSTALS and COPPER: a factory too.</item>
/// </list>
/// Every ore is MODERATE unless a test says otherwise, so no ore is short (D22) and only the gate's own rules buy anything.
/// </summary>
internal static class GateFixture
{
    /// <summary>The materials the gate needs, as X1-FJ91-I64 did.</summary>
    public static readonly string[] GateMaterials = ["FAB_MATS", "ADVANCED_CIRCUITRY"];

    /// <summary>The production chains of the gate's materials, and ALUMINUM's.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Chains = new Dictionary<string, IReadOnlyList<string>>
    {
        ["FAB_MATS"] = ["IRON", "QUARTZ_SAND"],
        ["IRON"] = ["IRON_ORE"],
        ["COPPER"] = ["COPPER_ORE"],
        ["ALUMINUM"] = ["ALUMINUM_ORE"],
        ["ELECTRONICS"] = ["SILICON_CRYSTALS", "COPPER"],
        ["MICROPROCESSORS"] = ["SILICON_CRYSTALS", "COPPER"],
        ["ADVANCED_CIRCUITRY"] = ["ELECTRONICS", "MICROPROCESSORS"],
    };

    /// <summary>The markets, with H51's IRON_ORE, F49's COPPER_ORE and H52's QUARTZ_SAND as given.</summary>
    /// <param name="iron">H51's supply of IRON_ORE.</param>
    /// <param name="copper">F49's supply of COPPER_ORE.</param>
    /// <param name="quartz">H52's supply of QUARTZ_SAND.</param>
    /// <returns>The markets.</returns>
    public static MarketSnapshot[] Markets(string iron = "MODERATE", string copper = "MODERATE", string quartz = "MODERATE") =>
    [
        Market(XB5C, Good("FUEL", "EXCHANGE", 97, 82, 180, "MODERATE")),
        Market(
            H51,
            Good("IRON_ORE", "IMPORT", 118, 58, 60, iron),
            Good("IRON", "EXPORT", 310, 150, 60, "MODERATE"),
            Good("FUEL", "EXCHANGE", 95, 80, 180, "MODERATE")),
        Market(
            H52,
            Good("IRON", "IMPORT", 320, 155, 60, "MODERATE"),
            Good("QUARTZ_SAND", "IMPORT", 52, 26, 60, quartz),
            Good("FAB_MATS", "EXPORT", 2_400, 1_200, 20, "MODERATE"),
            Good("FUEL", "EXCHANGE", 76, 69, 180, "MODERATE")),
        Market(
            F49,
            Good("COPPER_ORE", "IMPORT", 138, 67, 60, copper),
            Good("ALUMINUM_ORE", "IMPORT", 130, 63, 60, "MODERATE"),
            Good("COPPER", "EXPORT", 400, 200, 60, "MODERATE"),
            Good("ALUMINUM", "EXPORT", 380, 190, 60, "MODERATE"),
            Good("FUEL", "EXCHANGE", 82, 72, 180, "MODERATE")),
        Market(
            B7,
            Good("SILICON_CRYSTALS", "IMPORT", 100, 49, 60, "MODERATE"),
            Good("COPPER", "IMPORT", 420, 210, 60, "MODERATE"),
            Good("ELECTRONICS", "EXPORT", 3_000, 1_500, 20, "MODERATE"),
            Good("FUEL", "EXCHANGE", 79, 71, 180, "MODERATE")),
    ];

    /// <summary>The system's map while the gate needs these materials.</summary>
    /// <param name="materials">What the gate still needs; <see cref="GateMaterials"/> unless given.</param>
    /// <param name="markets">The markets; <see cref="Markets"/> unless given.</param>
    /// <returns>The map.</returns>
    public static TradeMarketMap Map(IEnumerable<string>? materials = null, MarketSnapshot[]? markets = null)
        => new(Waypoints, markets ?? Markets(), Chains)
        {
            ConstructionMaterials = new HashSet<string>(materials ?? GateMaterials, StringComparer.OrdinalIgnoreCase),
        };
}
