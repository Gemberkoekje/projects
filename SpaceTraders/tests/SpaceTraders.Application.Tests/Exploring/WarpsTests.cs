using FluentAssertions;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>
/// Slice 6.31 (D100: "Research how the warp works exactly, then measure, then fuel-safe."). The research note: a warp's fuel is a
/// flight's on the systems' distance (CRUISE the distance, BURN twice it); its seconds are round(round(distance) × multiplier /
/// engine speed + 15), the multiplier 50 in CRUISE and 25 in BURN; the drive's range caps it. D104, "CRUISE/BURN only": no warp
/// drifts. The explorer of 2026-10-06: an Ion Drive II of speed 36, an 800-unit tank, a Warp Drive I of range 2,000.
/// </summary>
public sealed class WarpsTests
{
    /// <summary>The explorer's modules as startup sync caches them: the API's modules, serialized.</summary>
    internal const string ExplorerModules = """
        [{"symbol":"MODULE_CARGO_HOLD_II","name":"Expanded Cargo Hold","description":"An expanded cargo hold.","capacity":40,"range":null,"requirements":{"power":2,"crew":2,"slots":2}},
         {"symbol":"MODULE_CREW_QUARTERS_I","name":"Crew Quarters","description":"Living space.","capacity":40,"range":null,"requirements":{"power":1,"crew":2,"slots":1}},
         {"symbol":"MODULE_WARP_DRIVE_I","name":"Warp Drive I","description":"A basic warp drive.","capacity":null,"range":2000,"requirements":{"power":3,"crew":2,"slots":1}},
         {"symbol":"MODULE_GAS_PROCESSOR_I","name":"Gas Processor","description":"Processes gases.","capacity":null,"range":null,"requirements":{"power":1,"crew":0,"slots":2}}]
        """;

    [Theory]
    [InlineData("CRUISE", 531, 531)]
    [InlineData("BURN", 531, 1062)]
    [InlineData("CRUISE", 840.4, 840)]
    [InlineData("CRUISE", 0.4, 1)]
    public void AWarpBurnsAFlightsFuel_OnTheSystemsDistance(string mode, double distance, int fuel)
        => Warps.Fuel(mode, distance).Should().Be(fuel);

    [Theory]
    [InlineData("CRUISE", 531, 753)]
    [InlineData("BURN", 531, 384)]
    [InlineData("CRUISE", 840, 1182)]
    [InlineData("CRUISE", 300, 432)]
    public void AWarpTakes_TheDistanceTimesFiftyOverTheSpeed_PlusFifteen_HalfThatInBurn(string mode, double distance, double seconds)
    {
        // X1-GT9 to X1-ZZ69, 531 apart, at the explorer's 36: 531 × 50 / 36 + 15 = 752.5, rounded 753.
        Warps.Seconds(mode, distance, 36).Should().Be(seconds);
    }

    [Fact]
    public void TheRange_IsTheWarpDrives_AsStartupSyncCachesTheModules()
    {
        var explorer = Explorer();

        Warps.Range(explorer).Should().Be(2000);
        Warps.HasDrive(explorer).Should().BeTrue();
    }

    [Fact]
    public void AShipWithoutAWarpDrive_OrWhoseModulesArentCached_HasNone()
    {
        Warps.HasDrive(Explorer() with { ModulesJson = """[{"symbol":"MODULE_CARGO_HOLD_II","capacity":40}]""" }).Should().BeFalse();
        Warps.HasDrive(Explorer() with { ModulesJson = null }).Should().BeFalse();
        Warps.Range(Explorer() with { ModulesJson = "not json" }).Should().Be(0);
    }

    [Fact]
    public void ADriveThatGivesNoRange_IsLimitedByTheFuelAlone()
        => Warps.Range(Explorer() with { ModulesJson = """[{"Symbol":"MODULE_WARP_DRIVE_II"}]""" }).Should().Be(int.MaxValue);

    [Theory]
    [InlineData(300, 800, 0, true, "BURN")]
    [InlineData(531, 800, 0, true, "CRUISE")]
    [InlineData(300, 800, 300, true, "CRUISE")]
    [InlineData(450, 800, 450, false, "CRUISE")]
    [InlineData(955, 800, 0, false, "CRUISE")]
    public void AWarp_BurnsWhereTheFuelPaysForIt_CruisesOtherwise_AndNeverDrifts(double distance, int fuel, int keep, bool warps, string mode)
    {
        // D104: "CRUISE/BURN only". X1-XJ90 lies 955 from home: an 800-unit tank warps there in neither.
        Warps.TryChooseMode(distance, fuel, keep, out var chosen).Should().Be(warps);
        chosen.Should().Be(mode);
    }

    private static ShipModel Explorer()
        => new("SPECTER-50", "X1-GT9", "X1-GT9-E10Z", "IN_ORBIT", "CRUISE", 800, 800, CargoCapacity: 40, ShipType: "SHIP_EXPLORER", ModulesJson: ExplorerModules, EngineJson: """{"symbol":"ENGINE_ION_DRIVE_II","speed":36}""");
}
