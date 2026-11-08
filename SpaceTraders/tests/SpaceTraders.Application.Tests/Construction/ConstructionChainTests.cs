using FluentAssertions;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Construction.ConstructionFixture;

namespace SpaceTraders.Application.Tests.Construction;

/// <summary>
/// Slice 6.51 (D139), asked on 2026-11-08 about what stops the gate: "Not just the gate mats themselves, also the materials
/// needed to build the gate mats. And the materials that they need, down to the ores." The chain is each material the gate
/// still needs and what it is made from, down to the ores, each with the best supply where it is sold and where the next
/// step is made from it.
/// </summary>
public sealed class ConstructionChainTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> MadeFrom = new(StringComparer.Ordinal)
    {
        ["FAB_MATS"] = ["IRON", "QUARTZ_SAND"],
        ["IRON"] = ["IRON_ORE"],
        ["ADVANCED_CIRCUITRY"] = ["ELECTRONICS", "MICROPROCESSORS"],
    };

    [Fact]
    public void TheChain_GoesDownToTheOres_EachWithWhereItIsSoldAndWhereTheNextStepIsMadeFromIt()
    {
        // F49 makes FAB_MATS from IRON (SCARCE there) and QUARTZ_SAND; D42 makes IRON from IRON_ORE, and an exchange at H51
        // sells IRON too, at HIGH; A1 imports IRON but makes nothing of the chain from it, so it doesn't count.
        var map = new TradeMarketMap(
            Waypoints,
            [
                Market(F49, Good("FAB_MATS", "EXPORT", 2_100, 1_050, 80, "MODERATE"), Good("IRON", "IMPORT", 300, 280, 60, "SCARCE"), Good("QUARTZ_SAND", "IMPORT", 90, 80, 60, "HIGH")),
                Market(D42, Good("IRON", "EXPORT", 150, 120, 60, "LIMITED"), Good("IRON_ORE", "IMPORT", 40, 35, 60, "ABUNDANT")),
                Market(H51, Good("IRON", "EXCHANGE", 200, 180, 60, "HIGH")),
                Market(A1, Good("IRON", "IMPORT", 320, 300, 60, "ABUNDANT")),
            ],
            MadeFrom);

        var chain = ConstructionChain.Of(map, Site(circuitry: 400), SystemSymbol);

        chain.Select(good => (good.TradeSymbol, good.Step, good.ExportSupply, good.ImportSupply)).Should().Equal(
            ("FAB_MATS", 1, "MODERATE", string.Empty),
            ("IRON", 2, "HIGH", "SCARCE"),
            ("QUARTZ_SAND", 2, string.Empty, "HIGH"),
            ("IRON_ORE", 3, string.Empty, "ABUNDANT"));
    }

    [Fact]
    public void AMaterialTheGateHasInFull_AndWhatItIsMadeFrom_AreNoLongerInTheChain()
    {
        var map = new TradeMarketMap(Waypoints, Markets(), MadeFrom);

        var chain = ConstructionChain.Of(map, Site(fabMats: 1_600), SystemSymbol);

        chain.Select(good => (good.TradeSymbol, good.Step)).Should().Equal(("ADVANCED_CIRCUITRY", 1), ("ELECTRONICS", 2), ("MICROPROCESSORS", 2));
        chain[0].ExportSupply.Should().Be("MODERATE", "D42 sells it");
    }

    [Fact]
    public void AMarketInAnotherSystem_DoesntCount()
    {
        var map = new TradeMarketMap(
            Waypoints,
            [Market("X1-HZ59-A1", Good("FAB_MATS", "EXPORT", 2_000, 1_000, 80, "ABUNDANT"))],
            MadeFrom);

        var chain = ConstructionChain.Of(map, new ConstructionSiteModel(Gate, false, [new ConstructionMaterialModel("FAB_MATS", 1_600, 0)]), SystemSymbol);

        chain.Single(good => good.TradeSymbol == "FAB_MATS").ExportSupply.Should().BeEmpty();
    }
}
