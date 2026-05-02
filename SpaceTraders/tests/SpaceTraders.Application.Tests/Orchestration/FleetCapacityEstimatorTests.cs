using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Orchestration;

public sealed class FleetCapacityEstimatorTests
{
    private static readonly IShipCapabilityRegistry Registry = new ShipCapabilityRegistry();

    [Fact]
    public async Task EstimateAsync_ClassifiesShipsByCapability()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            // Mining ship: SHIP_MINING_DRONE frame → CanMine = true
            new ShipModel("MINE-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100,
                CargoCapacity: 30, ShipType: "SHIP_MINING_DRONE"),
            // Cargo ship with no mining equipment → HaulingShip
            new ShipModel("HAUL-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 60),
            // No-cargo ship → ScoutingShip
            new ShipModel("PROBE-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100),
            // Unassigned ship → IdleShip
            new ShipModel("IDLE-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 20),
        ]);

        var assignments = Substitute.For<IShipAssignmentRepository>();
        var now = DateTimeOffset.UtcNow;
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipAssignmentDto("MINE-1", "Mine", null, null, null, null, 0, now, null),
            new ShipAssignmentDto("HAUL-1", "Contract", null, null, null, null, 0, now, null),
            new ShipAssignmentDto("PROBE-1", "MarketProbe", null, null, null, null, 0, now, null),
        ]);

        var estimator = new FleetCapacityEstimator(ships, assignments, Registry);
        var estimate = await estimator.EstimateAsync(CancellationToken.None);

        estimate.TotalShips.Should().Be(4);
        estimate.MiningShips.Should().Be(1);
        estimate.HaulingShips.Should().Be(1);
        estimate.TradingShips.Should().Be(0);
        estimate.ScoutingShips.Should().Be(1);
        estimate.IdleShips.Should().Be(1);
        estimate.TotalCargoCapacity.Should().Be(110);
        estimate.IdleCargoCapacity.Should().Be(20);
    }

    [Fact]
    public async Task EstimateAsync_TreatsUnassignedShipsAsIdle()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("FREE-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 40),
        ]);
        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var estimator = new FleetCapacityEstimator(ships, assignments, Registry);
        var estimate = await estimator.EstimateAsync(CancellationToken.None);

        estimate.IdleShips.Should().Be(1);
        estimate.IdleCargoCapacity.Should().Be(40);
    }

    [Fact]
    public async Task EstimateAsync_SiphonShipCountedAsMining()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("SIPHON-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100,
                ShipType: "SHIP_SIPHON_DRONE"),
        ]);
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var now = DateTimeOffset.UtcNow;
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipAssignmentDto("SIPHON-1", "Siphon", null, null, null, null, 0, now, null),
        ]);

        var estimator = new FleetCapacityEstimator(ships, assignments, Registry);
        var estimate = await estimator.EstimateAsync(CancellationToken.None);

        estimate.MiningShips.Should().Be(1);
        estimate.ScoutingShips.Should().Be(0);
        estimate.HaulingShips.Should().Be(0);
    }
}
