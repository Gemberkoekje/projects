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
}
