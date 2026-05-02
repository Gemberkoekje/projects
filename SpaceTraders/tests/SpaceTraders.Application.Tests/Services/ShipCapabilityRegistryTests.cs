using FluentAssertions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// Phase 12d: unit tests for <see cref="ShipCapabilityRegistry"/>.
/// </summary>
public sealed class ShipCapabilityRegistryTests
{
    private static readonly IShipCapabilityRegistry Registry = new ShipCapabilityRegistry();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ShipModel MakeShip(
        string shipType = "SHIP_PROBE",
        IReadOnlyList<string>? mounts = null,
        int cargoCapacity = 0,
        int fuelCapacity = 0,
        string? frameJson = null,
        string? modulesJson = null) =>
        new("SHIP-1", "X1-AB", "X1-AB-STATION", "IN_ORBIT", "CRUISE",
            FuelCurrent: fuelCapacity,
            FuelCapacity: fuelCapacity,
            CargoCapacity: cargoCapacity,
            MountSymbols: mounts,
            ShipType: shipType,
            FrameJson: frameJson,
            ModulesJson: modulesJson);

    // -------------------------------------------------------------------------
    // CanMine
    // -------------------------------------------------------------------------

    [Fact]
    public void GetCapabilities_CanMine_TrueForMiningDroneType()
    {
        var ship = MakeShip(shipType: "SHIP_MINING_DRONE", fuelCapacity: 100, cargoCapacity: 40);
        Registry.GetCapabilities(ship).CanMine.Should().BeTrue();
    }

    [Fact]
    public void GetCapabilities_CanMine_TrueForOreHoundType()
    {
        var ship = MakeShip(shipType: "SHIP_ORE_HOUND", fuelCapacity: 100, cargoCapacity: 40);
        Registry.GetCapabilities(ship).CanMine.Should().BeTrue();
    }

    [Fact]
    public void GetCapabilities_CanMine_TrueForMiningMount()
    {
        var ship = MakeShip(mounts: ["MOUNT_MINING_LASER_I"], fuelCapacity: 100, cargoCapacity: 40);
        Registry.GetCapabilities(ship).CanMine.Should().BeTrue();
    }

    [Fact]
    public void GetCapabilities_CanMine_TrueForSurveyorMount()
    {
        var ship = MakeShip(mounts: ["MOUNT_SURVEYOR_I"], fuelCapacity: 100, cargoCapacity: 40);
        Registry.GetCapabilities(ship).CanMine.Should().BeTrue();
    }

    [Fact]
    public void GetCapabilities_CanMine_FalseForProbeWithNoMiningMount()
    {
        var ship = MakeShip(shipType: "SHIP_PROBE", mounts: ["MOUNT_SENSOR_ARRAY_I"]);
        Registry.GetCapabilities(ship).CanMine.Should().BeFalse();
    }

    [Fact]
    public void GetCapabilities_CanMine_FalseForShipWithNoMounts()
    {
        var ship = MakeShip(shipType: "SHIP_LIGHT_HAULER");
        Registry.GetCapabilities(ship).CanMine.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // CanSiphon
    // -------------------------------------------------------------------------

    [Fact]
    public void GetCapabilities_CanSiphon_TrueForSiphonDroneType()
    {
        var ship = MakeShip(shipType: "SHIP_SIPHON_DRONE", fuelCapacity: 100);
        Registry.GetCapabilities(ship).CanSiphon.Should().BeTrue();
    }

    [Fact]
    public void GetCapabilities_CanSiphon_TrueForGasSiphonMount()
    {
        var ship = MakeShip(mounts: ["MOUNT_GAS_SIPHON_I"]);
        Registry.GetCapabilities(ship).CanSiphon.Should().BeTrue();
    }

    [Fact]
    public void GetCapabilities_CanSiphon_FalseForMiningDrone()
    {
        var ship = MakeShip(shipType: "SHIP_MINING_DRONE", mounts: ["MOUNT_MINING_LASER_I"]);
        Registry.GetCapabilities(ship).CanSiphon.Should().BeFalse();
    }

    [Fact]
    public void GetCapabilities_CanSiphon_FalseForShipWithNoMounts()
    {
        var ship = MakeShip(shipType: "SHIP_LIGHT_HAULER");
        Registry.GetCapabilities(ship).CanSiphon.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // CanSurvey
    // -------------------------------------------------------------------------

    [Fact]
    public void GetCapabilities_CanSurvey_TrueForSurveyorMount()
    {
        var ship = MakeShip(mounts: ["MOUNT_SURVEYOR_II"]);
        Registry.GetCapabilities(ship).CanSurvey.Should().BeTrue();
    }

    [Fact]
    public void GetCapabilities_CanSurvey_FalseForMiningMountWithoutSurveyor()
    {
        var ship = MakeShip(mounts: ["MOUNT_MINING_LASER_I"]);
        Registry.GetCapabilities(ship).CanSurvey.Should().BeFalse();
    }

    [Fact]
    public void GetCapabilities_CanSurvey_FalseForNoMounts()
    {
        var ship = MakeShip();
        Registry.GetCapabilities(ship).CanSurvey.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // HasCargo
    // -------------------------------------------------------------------------

    [Fact]
    public void GetCapabilities_HasCargo_TrueWhenCargoCapacityAboveZero()
    {
        var ship = MakeShip(cargoCapacity: 40);
        Registry.GetCapabilities(ship).HasCargo.Should().BeTrue();
    }

    [Fact]
    public void GetCapabilities_HasCargo_FalseWhenCargoCapacityIsZero()
    {
        var ship = MakeShip(cargoCapacity: 0);
        Registry.GetCapabilities(ship).HasCargo.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // HasFuelTank
    // -------------------------------------------------------------------------

    [Fact]
    public void GetCapabilities_HasFuelTank_TrueWhenFuelCapacityAboveZero()
    {
        var ship = MakeShip(fuelCapacity: 100);
        Registry.GetCapabilities(ship).HasFuelTank.Should().BeTrue();
    }

    [Fact]
    public void GetCapabilities_HasFuelTank_FalseWhenFuelCapacityIsZero()
    {
        var ship = MakeShip(fuelCapacity: 0);
        Registry.GetCapabilities(ship).HasFuelTank.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // CanRepair
    // -------------------------------------------------------------------------

    [Fact]
    public void GetCapabilities_CanRepair_TrueWhenFrameJsonIsPresent()
    {
        var ship = MakeShip(frameJson: "{\"symbol\":\"FRAME_MINER\",\"condition\":1.0,\"integrity\":1.0}");
        Registry.GetCapabilities(ship).CanRepair.Should().BeTrue();
    }

    [Fact]
    public void GetCapabilities_CanRepair_FalseWhenFrameJsonIsNull()
    {
        var ship = MakeShip(frameJson: null);
        Registry.GetCapabilities(ship).CanRepair.Should().BeFalse();
    }

    [Fact]
    public void GetCapabilities_CanRepair_FalseWhenFrameJsonIsEmpty()
    {
        var ship = MakeShip(frameJson: "");
        Registry.GetCapabilities(ship).CanRepair.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Multiple capabilities
    // -------------------------------------------------------------------------

    [Fact]
    public void GetCapabilities_AllFlags_SetCorrectly_ForFullyEquippedMiner()
    {
        var ship = MakeShip(
            shipType: "SHIP_ORE_HOUND",
            mounts: ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_I"],
            cargoCapacity: 60,
            fuelCapacity: 200,
            frameJson: "{\"symbol\":\"FRAME_MINER\",\"condition\":1.0,\"integrity\":1.0}");

        var caps = Registry.GetCapabilities(ship);

        caps.CanMine.Should().BeTrue();
        caps.CanSurvey.Should().BeTrue();
        caps.HasCargo.Should().BeTrue();
        caps.HasFuelTank.Should().BeTrue();
        caps.CanRepair.Should().BeTrue();
        caps.CanSiphon.Should().BeFalse();
    }

    [Fact]
    public void GetCapabilities_AllFlags_SetCorrectly_ForSiphonDrone()
    {
        var ship = MakeShip(
            shipType: "SHIP_SIPHON_DRONE",
            mounts: ["MOUNT_GAS_SIPHON_II"],
            cargoCapacity: 40,
            fuelCapacity: 0,
            frameJson: "{\"symbol\":\"FRAME_DRONE\",\"condition\":1.0,\"integrity\":1.0}");

        var caps = Registry.GetCapabilities(ship);

        caps.CanSiphon.Should().BeTrue();
        caps.HasCargo.Should().BeTrue();
        caps.CanRepair.Should().BeTrue();
        caps.HasFuelTank.Should().BeFalse();
        caps.CanMine.Should().BeFalse();
        caps.CanSurvey.Should().BeFalse();
    }

    [Fact]
    public void GetCapabilities_AllFlags_FalseForBareProbe()
    {
        var ship = MakeShip(shipType: "SHIP_PROBE");

        var caps = Registry.GetCapabilities(ship);

        caps.CanMine.Should().BeFalse();
        caps.CanSiphon.Should().BeFalse();
        caps.CanSurvey.Should().BeFalse();
        caps.HasCargo.Should().BeFalse();
        caps.HasFuelTank.Should().BeFalse();
        caps.CanRepair.Should().BeFalse();
    }
}
