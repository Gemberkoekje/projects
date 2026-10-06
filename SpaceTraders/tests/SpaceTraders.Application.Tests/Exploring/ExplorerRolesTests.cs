using FluentAssertions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>
/// Slice 6.30 (D102), asked on 2026-10-06: "Explorers can trade with 40 cargo space, so they can trade at the location they
/// are at until a new unexplored location comes up." X1-GT9-AE7B's SHIP_EXPLORER that day: an 800-unit tank, a 40-unit hold,
/// a warp drive, a gas processor, a sensor array and a gas siphon. By what it carries the plans took it for a siphon drone.
/// </summary>
public sealed class ExplorerRolesTests
{
    /// <summary>An explorer as the purchase caches it: its type, and no mounts until the next startup sync.</summary>
    private static readonly ShipModel Bought = new("SPECTER-50", "X1-GT9", "X1-GT9-AE7B", "DOCKED", "CRUISE", 800, 800, CargoCapacity: 40, ShipType: "SHIP_EXPLORER");

    /// <summary>The same explorer after startup sync: its registration role, its mounts, modules and frame.</summary>
    private static readonly ShipModel Synced = Bought with
    {
        ShipType = "EXPLORER",
        MountSymbols = ["MOUNT_SENSOR_ARRAY_II", "MOUNT_GAS_SIPHON_II"],
        ModulesJson = """[{"symbol":"MODULE_CARGO_HOLD_II"},{"symbol":"MODULE_WARP_DRIVE_I"},{"symbol":"MODULE_GAS_PROCESSOR_I"}]""",
        FrameJson = """{"symbol":"FRAME_EXPLORER"}""",
    };

    public static TheoryData<string> Explorers => new() { nameof(Bought), nameof(Synced) };

    [Theory]
    [MemberData(nameof(Explorers))]
    public void AnExplorer_OnlyTrades(string which)
    {
        var explorer = which == nameof(Bought) ? Bought : Synced;

        FleetRoles.IsExplorer(explorer).Should().BeTrue();
        FleetRoles.PotentialRoles(explorer).Should().Equal(FleetRole.Trade);
        FleetRoles.IsSiphoner(explorer).Should().BeFalse("the siphon plan's drones siphon, and its siphon purchases count them");
        FleetRoles.CanConstruct(explorer).Should().BeFalse();
        FleetRoles.IsCargoShip(explorer).Should().BeFalse("it is no cargo ship of Trade.ShipPurchases");
        FleetRoleBoard.For(rolesOn: false, surveyOn: true, spareTimeOn: true).IsSiphoner(explorer).Should().BeFalse("with the role board off as well");
        FleetRoleBoard.For(rolesOn: false, surveyOn: true, spareTimeOn: true).IsTrader(explorer).Should().BeTrue();
    }

    [Fact]
    public void ASiphonDrone_StillSiphons()
    {
        var drone = new ShipModel("SPECTER-9", "X1-FJ91", "X1-FJ91-C38", "IN_ORBIT", "CRUISE", 80, 80, CargoCapacity: 15, ShipType: "SHIP_SIPHON_DRONE");

        FleetRoles.IsExplorer(drone).Should().BeFalse();
        FleetRoles.IsSiphoner(drone).Should().BeTrue();
    }
}
