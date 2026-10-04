using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// Slice 6.4: before buying a drone or a hauler, the plans judge what it could reach and carry, from
/// the shipyard's listing of it.
/// </summary>
public sealed class ShipyardShipCapacityTests
{
    [Fact]
    public async Task AShipForSale_HasTheTankOfItsFrame_AndTheHoldOfItsCargoModules()
    {
        // As A2 listed the light hauler on 2026-10-02: two 40-unit holds, and crew quarters whose
        // capacity is crew, not cargo.
        await using var db = TestDbContextFactory.Create();
        var shipyards = new ShipyardRepository(db);
        await shipyards.UpsertAsync(new ShipyardDataModel(
            WaypointSymbol: "X1-AB-A2",
            SystemSymbol: "X1-AB",
            ShipTypesJson: """[{"type":"SHIP_LIGHT_HAULER"},{"type":"SHIP_PROBE"}]""",
            ShipsDetailJson: """
                [{"type":"SHIP_LIGHT_HAULER","purchasePrice":354210,
                  "frame":{"symbol":"FRAME_LIGHT_FREIGHTER","fuelCapacity":600},
                  "modules":[{"symbol":"MODULE_CARGO_HOLD_II","capacity":40},{"symbol":"MODULE_CARGO_HOLD_II","capacity":40},{"symbol":"MODULE_CREW_QUARTERS_I","capacity":40}]},
                 {"type":"SHIP_PROBE","purchasePrice":81645}]
                """));

        var shipyard = await shipyards.FindByWaypointAsync("X1-AB-A2");

        var hauler = shipyard!.Ships.Single(ship => ship.Type == "SHIP_LIGHT_HAULER");
        hauler.FuelCapacity.Should().Be(600);
        hauler.CargoCapacity.Should().Be(80);
        var probe = shipyard.Ships.Single(ship => ship.Type == "SHIP_PROBE");
        probe.FuelCapacity.Should().Be(0);
        probe.CargoCapacity.Should().Be(0);
    }

    /// <summary>
    /// Slice 2.11: the markets dashboard's shipyards table shows what each ship for sale carries: its mounts and modules,
    /// as the shipyard lists them.
    /// </summary>
    [Fact]
    public async Task AShipForSale_ListsItsMountsAndModules()
    {
        await using var db = TestDbContextFactory.Create();
        var shipyards = new ShipyardRepository(db);
        await shipyards.UpsertAsync(new ShipyardDataModel(
            WaypointSymbol: "X1-AB-H52",
            SystemSymbol: "X1-AB",
            ShipTypesJson: """[{"type":"SHIP_MINING_DRONE"},{"type":"SHIP_PROBE"}]""",
            ShipsDetailJson: """
                [{"type":"SHIP_MINING_DRONE","purchasePrice":46885,
                  "frame":{"symbol":"FRAME_DRONE","fuelCapacity":80},
                  "modules":[{"symbol":"MODULE_CARGO_HOLD_I","capacity":15},{"symbol":"MODULE_MINERAL_PROCESSOR_I"}],
                  "mounts":[{"symbol":"MOUNT_MINING_LASER_I","strength":3}]},
                 {"type":"SHIP_PROBE","purchasePrice":81645,"frame":{"symbol":"FRAME_PROBE","fuelCapacity":0},"modules":[],"mounts":[]}]
                """));

        var shipyard = await shipyards.FindByWaypointAsync("X1-AB-H52");

        var drone = shipyard!.Ships.Single(ship => ship.Type == "SHIP_MINING_DRONE");
        drone.Mounts.Should().Equal("MOUNT_MINING_LASER_I");
        drone.Modules.Should().Equal("MODULE_CARGO_HOLD_I", "MODULE_MINERAL_PROCESSOR_I");
        var probe = shipyard.Ships.Single(ship => ship.Type == "SHIP_PROBE");
        probe.Mounts.Should().BeEmpty();
        probe.Modules.Should().BeEmpty();
    }
}
