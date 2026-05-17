using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Integration.Test;

public sealed class ContractPlanSnapshotIntegrationTests
{
    private const string SnapshotFileName = "startup-snapshot-21-20260514-105213.json";

    [Fact]
    public async Task EnsureBootstrappedAsync_UsesSnapshotShipyardAndCredits_ToPurchaseMinerAndStartPlan()
    {
        var snapshot = await LoadSnapshotAsync(SnapshotFileName);

        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var port = Substitute.For<ISpaceTradersPort>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);

        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-SNAPSHOT-1",
                FactionSymbol: "COBALT",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(2),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("IRON_ORE", "X1-PT96-J59", 60, 0),
                }))
        ]);

        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel(
                Symbol: "SPECTER-DEBUG-2",
                SystemSymbol: "X1-PT96",
                WaypointSymbol: "X1-PT96-H53",
                Status: "DOCKED",
                FlightMode: "CRUISE",
                FuelCurrent: 0,
                FuelCapacity: 0,
                CargoCurrent: 0,
                CargoCapacity: 0,
                ShipType: "FRAME_PROBE",
                MountSymbols: [])
        ]);

        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel(
            Symbol: snapshot.Agent.Symbol,
            AccountId: snapshot.Agent.AccountId,
            HeadquartersSymbol: snapshot.Agent.Headquarters,
            Credits: snapshot.Agent.Credits,
            StartingFaction: snapshot.Agent.StartingFaction,
            ShipCount: snapshot.Agent.ShipCount));

        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<CancellationToken>())
            .Returns("X1-PT96-H53");

        var miningDrone = snapshot.Systems
            .SelectMany(s => s.Waypoints)
            .Select(w => w.Shipyard)
            .Where(s => s is not null)
            .SelectMany(s => s!.Ships)
            .First(s => string.Equals(s.Type, "SHIP_MINING_DRONE", StringComparison.OrdinalIgnoreCase));

        shipyards.FindByWaypointAsync("X1-PT96-H53", Arg.Any<CancellationToken>())
            .Returns(new ShipyardWaypointDto
            {
                WaypointSymbol = "X1-PT96-H53",
                SystemSymbol = "X1-PT96",
                ShipTypes = ["SHIP_MINING_DRONE", "SHIP_SURVEYOR"],
                Ships =
                [
                    new ShipyardShipDto
                    {
                        Type = "SHIP_MINING_DRONE",
                        PurchasePrice = miningDrone.PurchasePrice,
                    }
                ],
            });

        port.PurchaseShipAsync("SHIP_MINING_DRONE", "X1-PT96-H53", Arg.Any<CancellationToken>())
            .Returns(new PurchaseShipActionResult(
                Agent: new AgentModel(
                    Symbol: snapshot.Agent.Symbol,
                    AccountId: snapshot.Agent.AccountId,
                    HeadquartersSymbol: snapshot.Agent.Headquarters,
                    Credits: snapshot.Agent.Credits - (int)miningDrone.PurchasePrice,
                    StartingFaction: snapshot.Agent.StartingFaction,
                    ShipCount: snapshot.Agent.ShipCount + 1),
                ShipSymbol: "SPECTER-DEBUG-3",
                ShipNav: new NavModel("DOCKED", "X1-PT96", "X1-PT96-H53", "CRUISE", null, null),
                ShipFuel: new FuelModel(80, 80),
                ShipCargo: new CargoModel(0, 20, []),
                Cost: (int)miningDrone.PurchasePrice));

        waypoints.GetBySystemAsync("X1-PT96", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel(
                Symbol: "X1-PT96-B11",
                SystemSymbol: "X1-PT96",
                Type: "ASTEROID",
                X: -286,
                Y: -195,
                HasMarket: false,
                HasShipyard: false,
                LastObservedAt: DateTimeOffset.UtcNow,
                TraitsJson: "MINERAL_DEPOSITS IRON_ORE"),
            new WaypointCacheModel(
                Symbol: "X1-PT96-J59",
                SystemSymbol: "X1-PT96",
                Type: "ASTEROID_BASE",
                X: -201,
                Y: 694,
                HasMarket: true,
                HasShipyard: false,
                LastObservedAt: DateTimeOffset.UtcNow,
                TraitsJson: "MARKETPLACE")
        ]);

        var shipPurchases = Substitute.For<IShipPurchaseService>();
        shipPurchases.TryPurchaseAsync("SHIP_MINING_DRONE", "X1-PT96-H53", Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult
            {
                IsSuccess = true,
                PurchasedShip = new ShipModel("SPECTER-DEBUG-3", "X1-PT96", "X1-PT96-H53", "DOCKED", "CRUISE", 100, 100, ShipType: "SHIP_MINING_DRONE", CargoCapacity: 40),
                EstimatedCost = 64_200,
                ActualCost = 64_200,
            });

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            shipyards,
            waypoints,
            port,
            shipPurchases,
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await port.Received(1).PurchaseShipAsync("SHIP_MINING_DRONE", "X1-PT96-H53", Arg.Any<CancellationToken>());

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.Active
                && p.ContractId == "C-SNAPSHOT-1"
                && p.ShipSymbol == "SPECTER-DEBUG-3"
                && p.SourceWaypoint == "X1-PT96-B11"
                && p.DestinationWaypoint == "X1-PT96-J59"
                && p.UnitsRequired == 60
                && p.UnitsFulfilled == 0),
            Arg.Any<CancellationToken>());

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.AssignmentType == "Contract"
                && a.ShipSymbol == "SPECTER-DEBUG-3"
                && a.OriginWaypoint == "X1-PT96-B11"
                && a.DestWaypoint == "X1-PT96-J59"
                && a.CargoSymbol == "IRON_ORE"
                && a.ContractId == "C-SNAPSHOT-1"
                && a.RequiredUnits == 60),
            Arg.Any<CancellationToken>());

        await plans.DidNotReceive().UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p => p.Status == ContractMineralPlanStatus.PendingBudget),
            Arg.Any<CancellationToken>());
    }

    private static async Task<StartupSnapshot> LoadSnapshotAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        var json = await File.ReadAllTextAsync(path);
        var snapshot = JsonSerializer.Deserialize<StartupSnapshot>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        snapshot.Should().NotBeNull();
        return snapshot!;
    }

    private sealed class StartupSnapshot
    {
        public required SnapshotAgent Agent { get; init; }

        public required List<SnapshotSystem> Systems { get; init; }
    }

    private sealed class SnapshotAgent
    {
        public string? AccountId { get; init; }

        public required string Symbol { get; init; }

        public string? Headquarters { get; init; }

        public required int Credits { get; init; }

        public required string StartingFaction { get; init; }

        public required int ShipCount { get; init; }
    }

    private sealed class SnapshotSystem
    {
        public required List<SnapshotWaypointContainer> Waypoints { get; init; }
    }

    private sealed class SnapshotWaypointContainer
    {
        public SnapshotShipyard? Shipyard { get; init; }
    }

    private sealed class SnapshotShipyard
    {
        public required List<SnapshotShipyardShip> Ships { get; init; }
    }

    private sealed class SnapshotShipyardShip
    {
        public required string Type { get; init; }

        public required int PurchasePrice { get; init; }
    }
}
