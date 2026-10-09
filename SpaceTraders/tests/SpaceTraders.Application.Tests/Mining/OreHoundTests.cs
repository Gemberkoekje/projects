using FluentAssertions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>
/// Slice 6.39 (D120), asked on 2026-10-09: "When available, use ORE HOUNDS instead of MINING DRONES. I think they can both survey
/// and mine, so have them survey until the desired mineral is found, then mine the survey." An ore hound is a mining drone in
/// every rule; it surveys only for its own trips, so it takes no survey role and needs no survey ship.
/// </summary>
public sealed class OreHoundTests
{
    [Theory]
    [InlineData("SHIP_ORE_HOUND", null)]
    [InlineData("EXCAVATOR", """{"symbol":"FRAME_MINER","fuelCapacity":400}""")]
    public void AnOreHound_AsBought_OrByItsFrame_IsAMiningDroneThatSurveysForItself(string shipType, string? frame)
    {
        var hound = OreHound() with { ShipType = shipType, FrameJson = frame };

        FleetRoles.IsOreHound(hound).Should().BeTrue();
        FleetRoles.IsMiningDrone(hound).Should().BeTrue();
        FleetRoles.SurveysForItself(hound).Should().BeTrue();
        FleetRoles.NeedsSurveyShips(hound).Should().BeFalse();
        FleetRoles.IsSurveyor(hound, surveyPlanOn: true).Should().BeFalse("it surveys only for its own trips");
        FleetRoles.IsMiner(hound, surveyPlanOn: true).Should().BeTrue();
        FleetRoles.PotentialRoles(hound).Should().Equal(FleetRole.Mine, FleetRole.Trade);
        FleetRoles.CanConstruct(hound).Should().BeFalse("a drone gathers first (D58)");
    }

    [Fact]
    public void AnOreHoundWithoutASurveyor_IsAMiningDroneTheSurveyShipsServe()
    {
        var hound = OreHound() with { MountSymbols = ["MOUNT_MINING_LASER_II"] };

        FleetRoles.IsMiningDrone(hound).Should().BeTrue();
        FleetRoles.SurveysForItself(hound).Should().BeFalse();
        FleetRoles.NeedsSurveyShips(hound).Should().BeTrue();
    }

    [Fact]
    public void TheCommandShip_IsNoOreHound_AndStillSurveys()
    {
        var command = new ShipModel(
            "SHIP-1", "X1-AB", "X1-AB-A1", "DOCKED", "CRUISE", 400, 400, CargoCapacity: 40, ShipType: "COMMAND",
            MountSymbols: ["MOUNT_SURVEYOR_II", "MOUNT_MINING_LASER_II"], FrameJson: """{"symbol":"FRAME_FRIGATE"}""");

        FleetRoles.IsOreHound(command).Should().BeFalse();
        FleetRoles.IsMiningDrone(command).Should().BeFalse();
        FleetRoles.IsSurveyor(command, surveyPlanOn: true).Should().BeTrue();
        FleetRoles.PotentialRoles(command).Should().StartWith(FleetRole.Survey);
    }

    [Fact]
    public void TheMinerBought_IsAnOreHoundWhereOneIsSold_ThoughADroneCostsLess()
    {
        var listing = MinerShips.Cheapest([Shipyard("X1-AB-A1", Drone(48_000)), Shipyard("X1-AB-A2", Hound(210_000), Drone(47_000))]);

        listing.Should().NotBeNull();
        (listing.Value.Shipyard.WaypointSymbol, listing.Value.Ship.Type).Should().Be(("X1-AB-A2", "SHIP_ORE_HOUND"));
    }

    [Fact]
    public void TheMinerBought_IsADrone_WhereEveryOreHoundIsScarce()
    {
        // D121: "As with the other ships, do not buy ... if the supply is SCARCE."
        var listing = MinerShips.Cheapest([Shipyard("X1-AB-A1", Drone(48_000)), Shipyard("X1-AB-A2", Hound(210_000) with { Supply = "SCARCE" })]);

        (listing!.Value.Shipyard.WaypointSymbol, listing.Value.Ship.Type).Should().Be(("X1-AB-A1", "SHIP_MINING_DRONE"));
    }

    [Fact]
    public void NoMinerIsBought_WhereNoneIsSoldButAtScarce()
        => MinerShips.Cheapest([Shipyard("X1-AB-A1", Drone(48_000) with { Supply = "SCARCE" })]).Should().BeNull();

    [Fact]
    public void AnOreHoundAsBought_CarriesItsListingsTankHoldAndMounts()
    {
        var bought = MinerShips.AsBought(Shipyard("X1-AB-A2"), Hound(210_000));

        (bought.ShipType, bought.FuelCapacity, bought.CargoCapacity, bought.WaypointSymbol).Should().Be(("SHIP_ORE_HOUND", 400, 30, "X1-AB-A2"));
        FleetRoles.SurveysForItself(bought).Should().BeTrue();
    }

    private static ShipModel OreHound()
        => new("SHIP-7", "X1-AB", "X1-AB-AST", "IN_ORBIT", "CRUISE", 400, 400, CargoCapacity: 30, ShipType: "SHIP_ORE_HOUND",
            MountSymbols: ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_I"]);

    private static ShipyardShipDto Drone(long price)
        => new() { Type = "SHIP_MINING_DRONE", PurchasePrice = price, FuelCapacity = 80, CargoCapacity = 15, Mounts = ["MOUNT_MINING_LASER_I"] };

    private static ShipyardShipDto Hound(long price)
        => new() { Type = "SHIP_ORE_HOUND", PurchasePrice = price, FuelCapacity = 400, CargoCapacity = 30, Mounts = ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_I"] };

    private static ShipyardWaypointDto Shipyard(string waypoint, params ShipyardShipDto[] ships) => new()
    {
        WaypointSymbol = waypoint,
        SystemSymbol = "X1-AB",
        ShipTypes = [.. ships.Select(ship => ship.Type)],
        Ships = ships,
    };
}
