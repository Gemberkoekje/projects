using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events;
using SpaceTraders.Application.Tests.Services;
using Wolverine;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class ContractPlanServiceTests
{
    [Fact]
    public async Task EnsureBootstrappedAsync_CreatesDeferredPlan_WhenDeliverableIsNonMineral()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-1",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(2),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("FOOD", "X1-AB-MKT", 40, 0),
                }))
        ]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.DeferredUnsupported
                && p.ContractId == "C-1"
                && p.TradeSymbol == "FOOD"
                && p.ShipSymbol == string.Empty),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_CreatesActivePlan_WithIdleMinerShip()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-2",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("IRON_ORE", "X1-AB-MKT", 60, 10),
                }))
        ]);

        var idleMiner = new ShipModel(
            Symbol: "SHIP-MINER-1",
            SystemSymbol: "X1-AB",
            WaypointSymbol: "X1-AB-START",
            Status: "DOCKED",
            FlightMode: "CRUISE",
            FuelCurrent: 100,
            FuelCapacity: 100,
            CargoCurrent: 0,
            CargoCapacity: 40,
            ShipType: "SHIP_MINING_DRONE",
            MountSymbols: ["MOUNT_MINING_LASER_I"]);

        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([idleMiner]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-AST", "X1-AB", "ASTEROID_FIELD", 0, 0, false, false, DateTimeOffset.UtcNow, TraitsJson: "IRON_ORE"),
            new WaypointCacheModel("X1-AB-MKT", "X1-AB", "PLANET", 1, 1, true, false, DateTimeOffset.UtcNow)
        ]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            Substitute.For<IShipyardRepository>(),
            waypoints,
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.Active
                && p.ContractId == "C-2"
                && p.ShipSymbol == "SHIP-MINER-1"
                && p.TradeSymbol == "IRON_ORE"
                && p.SourceWaypoint == "X1-AB-AST"
                && p.DestinationWaypoint == "X1-AB-MKT"
                && p.UnitsRequired == 60
                && p.UnitsFulfilled == 10),
            Arg.Any<CancellationToken>());

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.AssignmentType == "Contract"
                && a.ShipSymbol == "SHIP-MINER-1"
                && a.OriginWaypoint == "X1-AB-AST"
                && a.DestWaypoint == "X1-AB-MKT"
                && a.CargoSymbol == "IRON_ORE"
                && a.ContractId == "C-2"
                && a.RequiredUnits == 50),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnIdleMinerAbroad_IsNotTheContractsShip()
    {
        // Slice 6.40 (D122): a drone abroad mines there, for the mining plan; the contract works at home, where no miner is idle.
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var agents = Substitute.For<IAgentRepository>();
        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-2",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("IRON_ORE", "X1-AB-MKT", 60, 10) }))
        ]);
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, "X1-AB-A1", 1_000_000, "COBALT", 3));
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("SHIP-MINER-1", "X1-CD", "X1-CD-START", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 40, ShipType: "SHIP_MINING_DRONE", MountSymbols: ["MOUNT_MINING_LASER_I"]),
        ]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            agents,
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await plans.DidNotReceive().UpsertAsync(Arg.Is<ContractMineralPlanState>(p => p.ShipSymbol == "SHIP-MINER-1"), Arg.Any<CancellationToken>());
        await assignments.DidNotReceive().UpsertAsync(Arg.Is<ShipAssignmentDto>(a => a.ShipSymbol == "SHIP-MINER-1"), Arg.Any<CancellationToken>());
        await plans.Received(1).UpsertAsync(Arg.Is<ContractMineralPlanState>(p => p.Status == ContractMineralPlanStatus.PendingBudget), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_CreatesPendingBudgetPlan_WhenNoMinerAndCannotPurchase()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var shipPurchases = Substitute.For<IShipPurchaseService>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-3",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("COPPER_ORE", "X1-AB-MKT", 30, 0),
                }))
        ]);

        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel(
            Symbol: "AGENT",
            AccountId: null,
            HeadquartersSymbol: null,
            Credits: 1,
            StartingFaction: "COSMIC",
            ShipCount: 1));

        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");
        shipyards.FindByWaypointAsync("X1-AB-SHIPYARD", Arg.Any<CancellationToken>()).Returns(new ShipyardWaypointDto
        {
            WaypointSymbol = "X1-AB-SHIPYARD",
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_MINING_DRONE"],
            Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 100_000 }],
        });

        shipPurchases.TryPurchaseAsync("SHIP_MINING_DRONE", "X1-AB-SHIPYARD", Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult
            {
                IsSuccess = false,
                FailureReason = "Insufficient credits.",
                EstimatedCost = 100_000,
            });

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            shipyards,
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            shipPurchases,
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.PendingBudget
                && p.ContractId == "C-3"
                && p.ShipSymbol == string.Empty
                && p.TradeSymbol == "COPPER_ORE"),
            Arg.Any<CancellationToken>());

        await assignments.DidNotReceive().UpsertAsync(Arg.Any<ShipAssignmentDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheContractsDrone_IsNotBought_NorANeed_WhereTheShipyardHasItScarce()
    {
        // D121, asked on 2026-10-09: "As with the other ships, do not buy INTERCEPTORS if the supply is SCARCE", then "Every
        // purchase". The home shipyard has the drone SCARCE, as cached.
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var shipPurchases = Substitute.For<IShipPurchaseService>();
        var order = new OpenPurchaseOrder();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-3",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("COPPER_ORE", "X1-AB-MKT", 30, 0),
                }))
        ]);
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, "X1-AB-A1", 1_000_000, "COSMIC", 1));
        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");
        shipyards.FindByWaypointAsync("X1-AB-SHIPYARD", Arg.Any<CancellationToken>()).Returns(new ShipyardWaypointDto
        {
            WaypointSymbol = "X1-AB-SHIPYARD",
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_MINING_DRONE"],
            Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 100_000, Supply = "SCARCE" }],
        });

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            shipyards,
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            shipPurchases,
            agents,
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            order,
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        order.Of(AutomationPlan.Contract).Should().Be(PurchaseNeed.None);
        await shipPurchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task TheContractsMiner_IsAnOreHound_WhereAShipyardAtHomeSellsOne()
    {
        // Slice 6.39 (D120), asked on 2026-10-09: "When available, use ORE HOUNDS instead of MINING DRONES." The home shipyard
        // sells drones; another at home sells ore hounds.
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var shipPurchases = Substitute.For<IShipPurchaseService>();
        var order = new OpenPurchaseOrder();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-3",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("COPPER_ORE", "X1-AB-MKT", 30, 0),
                }))
        ]);
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, "X1-AB-A1", 1_000_000, "COSMIC", 1));
        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");
        shipyards.FindByWaypointAsync("X1-AB-SHIPYARD", Arg.Any<CancellationToken>()).Returns(new ShipyardWaypointDto
        {
            WaypointSymbol = "X1-AB-SHIPYARD",
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_MINING_DRONE"],
            Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 100_000 }],
        });
        shipyards.FindShipyardForTypeAsync("SHIP_ORE_HOUND", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns("X1-AB-YARD2");
        shipyards.FindByWaypointAsync("X1-AB-YARD2", Arg.Any<CancellationToken>()).Returns(new ShipyardWaypointDto
        {
            WaypointSymbol = "X1-AB-YARD2",
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_ORE_HOUND"],
            Ships = [new ShipyardShipDto { Type = "SHIP_ORE_HOUND", PurchasePrice = 210_000 }],
        });
        shipPurchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = false, Failure = ShipPurchaseFailure.OverBudget, FailureReason = "over budget" });

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            shipyards,
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            shipPurchases,
            agents,
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            order,
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        order.Of(AutomationPlan.Contract).Should().Be(new PurchaseNeed(PurchaseTier.Contract, "SHIP_ORE_HOUND", "X1-AB-YARD2", 210_000));
        await shipPurchases.Received(1).TryPurchaseAsync("SHIP_ORE_HOUND", "X1-AB-YARD2", Arg.Any<CancellationToken>());
        await shipPurchases.DidNotReceive().TryPurchaseAsync("SHIP_MINING_DRONE", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheContractsDrone_IsLookedForAtHome_NotWhereTheCommandShipExplores_OrAProbeWatches()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var shipPurchases = Substitute.For<IShipPurchaseService>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-3",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("COPPER_ORE", "X1-AB-MKT", 30, 0),
                }))
        ]);

        // Asked on 2026-10-04: business stays home. The shipyard seen last that sells drones can be one the command ship has
        // just explored, in X1-KR90, or one where a probe watches the markets, in X1-CD (slice 6.28, which found the probe made
        // X1-CD a system the plans did business in): the drone isn't bought there.
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipModel("SHIP-1", "X1-KR90", "X1-KR90-YARD", "DOCKED", "CRUISE", 400, 400, CargoCapacity: 40, ShipType: "COMMAND"),
            new ShipModel("SHIP-2", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 0, 0, ShipType: "SATELLITE"),
            new ShipModel("SHIP-3", "X1-CD", "X1-CD-YARD", "DOCKED", "CRUISE", 0, 0, ShipType: "SHIP_PROBE"),
        ]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-1", "Explore", "X1-KR90-YARD", null, null, null, 0, DateTimeOffset.UtcNow, null)]);

        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel(
            Symbol: "AGENT",
            AccountId: null,
            HeadquartersSymbol: "X1-AB-A1",
            Credits: 1,
            StartingFaction: "COSMIC",
            ShipCount: 3));

        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");
        shipyards.FindByWaypointAsync("X1-AB-SHIPYARD", Arg.Any<CancellationToken>()).Returns(new ShipyardWaypointDto
        {
            WaypointSymbol = "X1-AB-SHIPYARD",
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_MINING_DRONE"],
            Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 100_000 }],
        });

        shipPurchases.TryPurchaseAsync("SHIP_MINING_DRONE", "X1-AB-SHIPYARD", Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult
            {
                IsSuccess = false,
                FailureReason = "Insufficient credits.",
                EstimatedCost = 100_000,
            });

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            shipyards,
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            shipPurchases,
            agents,
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await shipyards.Received().FindShipyardForTypeAsync(
            "SHIP_MINING_DRONE",
            Arg.Is<IReadOnlyCollection<string>>(systems => systems.SequenceEqual(new[] { "X1-AB" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_WhileThePlanWaitsForBudget_KeepsALogLineOnlyWhenItStartsWaiting()
    {
        // B12: a plan waiting for budget is retried on every tick, which wrote four lines at
        // Information each time.
        var log = new LogRecorder();
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var shipPurchases = Substitute.For<IShipPurchaseService>();
        ContractMineralPlanState? stored = null;
        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => stored);
        await plans.UpsertAsync(Arg.Do<ContractMineralPlanState>(plan => stored = plan), Arg.Any<CancellationToken>());
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-3",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("COPPER_ORE", "X1-AB-MKT", 30, 0) }))
        ]);
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100)]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");
        shipyards.FindByWaypointAsync("X1-AB-SHIPYARD", Arg.Any<CancellationToken>()).Returns(new ShipyardWaypointDto
        {
            WaypointSymbol = "X1-AB-SHIPYARD",
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_MINING_DRONE"],
            Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 100_000 }],
        });
        shipPurchases.TryPurchaseAsync("SHIP_MINING_DRONE", "X1-AB-SHIPYARD", Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = false, FailureReason = "Insufficient credits.", EstimatedCost = 100_000 });
        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            shipyards,
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            shipPurchases,
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            log.For<ContractPlanService>());

        for (var tick = 0; tick < 12; tick++)
        {
            await sut.EnsureBootstrappedAsync(CancellationToken.None);
        }

        stored!.Status.Should().Be(ContractMineralPlanStatus.PendingBudget);
        log.Kept.Should().ContainSingle().Which.Should().Contain("C-3");
        log.Journal.Should().ContainSingle(e => e.EventKind == "PlanBlocked" && Equals(e.Properties["Reason"], "no_ship_or_budget"));
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_WhileThePlanWaitsForBudget_CallsNoApiAndKeepsThePlan()
    {
        // B27: a plan waiting for budget is retried on every tick, and each retry fetched every
        // contract from the API (12 calls a minute) and saved the plan again under a new id. The
        // contract it waits for is cached already; only the ship is worth trying again.
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var shipPurchases = Substitute.For<IShipPurchaseService>();
        var port = Substitute.For<ISpaceTradersPort>();
        ContractMineralPlanState? stored = null;
        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => stored);
        await plans.UpsertAsync(Arg.Do<ContractMineralPlanState>(plan => stored = plan), Arg.Any<CancellationToken>());
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-3",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("COPPER_ORE", "X1-AB-MKT", 30, 0) }))
        ]);
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100)]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");
        shipyards.FindByWaypointAsync("X1-AB-SHIPYARD", Arg.Any<CancellationToken>()).Returns(new ShipyardWaypointDto
        {
            WaypointSymbol = "X1-AB-SHIPYARD",
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_MINING_DRONE"],
            Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 100_000 }],
        });
        shipPurchases.TryPurchaseAsync("SHIP_MINING_DRONE", "X1-AB-SHIPYARD", Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = false, FailureReason = "Insufficient credits.", EstimatedCost = 100_000 });
        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            shipyards,
            Substitute.For<IWaypointRepository>(),
            port,
            shipPurchases,
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);
        var waiting = stored;
        waiting!.Status.Should().Be(ContractMineralPlanStatus.PendingBudget);
        port.ClearReceivedCalls();
        plans.ClearReceivedCalls();
        shipPurchases.ClearReceivedCalls();

        for (var tick = 0; tick < 12; tick++)
        {
            await sut.EnsureBootstrappedAsync(CancellationToken.None);
        }

        port.ReceivedCalls().Should().BeEmpty();
        await plans.DidNotReceive().UpsertAsync(Arg.Any<ContractMineralPlanState>(), Arg.Any<CancellationToken>());
        stored.Should().BeSameAs(waiting);
        await shipPurchases.Received(12).TryPurchaseAsync("SHIP_MINING_DRONE", "X1-AB-SHIPYARD", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_DoesNothing_WhenPlanAlreadyExists()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-4",
            ShipSymbol = "SHIP-MINER-1",
            TradeSymbol = "IRON_ORE",
            SourceWaypoint = "X1-AB-AST",
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 10,
            UnitsFulfilled = 0,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var contracts = Substitute.For<IContractRepository>();
        var order = new OpenPurchaseOrder();

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            order,
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await contracts.DidNotReceive().GetActiveAsync(Arg.Any<CancellationToken>());

        // Slice 6.10b (D43): a pass that needs no drone says so, so the plans after it in the order may buy.
        order.Needs.Should().ContainKey(AutomationPlan.Contract).WhoseValue.Should().Be(PurchaseNeed.None);
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_RequestsPurchase_WhenNoIdleMinerAndFundsAvailable()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var port = Substitute.For<ISpaceTradersPort>();
        var shipPurchases = Substitute.For<IShipPurchaseService>();
        var order = new OpenPurchaseOrder();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-5",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("IRON_ORE", "X1-AB-MKT", 20, 0),
                }))
        ]);

        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel(
            Symbol: "AGENT",
            AccountId: null,
            HeadquartersSymbol: null,
            Credits: 500_000,
            StartingFaction: "COSMIC",
            ShipCount: 1));

        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");
        shipyards.FindByWaypointAsync("X1-AB-SHIPYARD", Arg.Any<CancellationToken>()).Returns(new ShipyardWaypointDto
        {
            WaypointSymbol = "X1-AB-SHIPYARD",
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_MINING_DRONE"],
            Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 100_000 }],
        });

        shipPurchases.TryPurchaseAsync("SHIP_MINING_DRONE", "X1-AB-SHIPYARD", Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult
            {
                IsSuccess = true,
                EstimatedCost = 100_000,
                ActualCost = 100_000,
                PurchasedShip = new ShipModel(
                    Symbol: "SHIP-NEW-MINER",
                    SystemSymbol: "X1-AB",
                    WaypointSymbol: "X1-AB-SHIPYARD",
                    Status: "DOCKED",
                    FlightMode: "CRUISE",
                    FuelCurrent: 80,
                    FuelCapacity: 100,
                    CargoCurrent: 0,
                    CargoCapacity: 20,
                    ShipType: "SHIP_MINING_DRONE",
                    MountSymbols: ["MOUNT_MINING_LASER_I"]),
            });

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-AST", "X1-AB", "ASTEROID_FIELD", 0, 0, false, false, DateTimeOffset.UtcNow, TraitsJson: "IRON_ORE"),
            new WaypointCacheModel("X1-AB-MKT", "X1-AB", "PLANET", 1, 1, true, false, DateTimeOffset.UtcNow)
        ]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            shipyards,
            waypoints,
            port,
            shipPurchases,
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            order,
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        // Slice 6.10b (D43): the contract's drone comes first in the order ships are bought in.
        order.Of(AutomationPlan.Contract).Should().Be(new PurchaseNeed(PurchaseTier.Contract, "SHIP_MINING_DRONE", "X1-AB-SHIPYARD", 100_000));
        await shipPurchases.Received(1).TryPurchaseAsync("SHIP_MINING_DRONE", "X1-AB-SHIPYARD", Arg.Any<CancellationToken>());
        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.Active
                && p.ShipSymbol == "SHIP-NEW-MINER"
                && p.ContractId == "C-5"),
            Arg.Any<CancellationToken>());

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.AssignmentType == "Contract"
                && a.ShipSymbol == "SHIP-NEW-MINER"
                && a.RequiredUnits == 20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_RefreshesContractsFromApi_OncePerCycle()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var port = Substitute.For<ISpaceTradersPort>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);

        port.GetMyContractsAsync(1, 20, Arg.Any<CancellationToken>()).Returns(
            new PagedResult<ContractModel>([
                new ContractModel(
                    Id: "C-API-1",
                    FactionSymbol: "COSMIC",
                    Type: "PROCUREMENT",
                    IsAccepted: true,
                    IsFulfilled: false,
                    Expiration: DateTimeOffset.UtcNow.AddDays(3),
                    DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                    TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                    Deliverables:
                    [
                        new ContractDeliverableModel("IRON_ORE", "X1-AB-MKT", 20, 0),
                    ])
            ], 1, 1, 20));

        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-API-1",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("IRON_ORE", "X1-AB-MKT", 20, 0),
                }))
        ]);

        var idleMiner = new ShipModel(
            Symbol: "SHIP-MINER-1",
            SystemSymbol: "X1-AB",
            WaypointSymbol: "X1-AB-START",
            Status: "DOCKED",
            FlightMode: "CRUISE",
            FuelCurrent: 100,
            FuelCapacity: 100,
            CargoCurrent: 0,
            CargoCapacity: 40,
            ShipType: "SHIP_MINING_DRONE",
            MountSymbols: ["MOUNT_MINING_LASER_I"]);

        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([idleMiner]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var waypoints = Substitute.For<IWaypointRepository>();
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-AST", "X1-AB", "ASTEROID_FIELD", 0, 0, false, false, DateTimeOffset.UtcNow, TraitsJson: "IRON_ORE")
        ]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            Substitute.For<IShipyardRepository>(),
            waypoints,
            port,
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await port.Received(1).GetMyContractsAsync(1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_Negotiates_WhenNoContractsExist()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var port = Substitute.For<ISpaceTradersPort>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        port.GetMyContractsAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ContractModel>([], 0, 1, 20));

        var firstRead = new List<ContractDto>();
        var secondRead = new List<ContractDto>
        {
            new(
                Id: "C-NEG-1",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("IRON_ORE", "X1-AB-MKT", 10, 0),
                }))
        };

        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(firstRead, secondRead);

        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("SHIP-NEGOTIATOR", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100)
        ]);

        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        port.NegotiateContractAsync("SHIP-NEGOTIATOR", Arg.Any<CancellationToken>())
            .Returns(new NegotiateContractActionResult(
                new ContractModel(
                    Id: "C-NEG-1",
                    FactionSymbol: "COSMIC",
                    Type: "PROCUREMENT",
                    IsAccepted: true,
                    IsFulfilled: false,
                    Expiration: DateTimeOffset.UtcNow.AddDays(3),
                    DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                    TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                    Deliverables: [new ContractDeliverableModel("IRON_ORE", "X1-AB-MKT", 10, 0)])));

        var waypoints = Substitute.For<IWaypointRepository>();
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-AST", "X1-AB", "ASTEROID_FIELD", 0, 0, false, false, DateTimeOffset.UtcNow, TraitsJson: "IRON_ORE")
        ]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            Substitute.For<IShipyardRepository>(),
            waypoints,
            port,
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await port.Received(1).NegotiateContractAsync("SHIP-NEGOTIATOR", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_AcceptsContract_WhenPendingContractNotAccepted_AndRecordsThePayment()
    {
        // B33: the acceptance payment never reached the cached credits that purchases are budgeted
        // from; only the next restart's sync brought them up to date.
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var port = Substitute.For<ISpaceTradersPort>();
        var bus = Substitute.For<IMessageBus>();
        var log = new LogRecorder();
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel(Symbol: "AGENT", AccountId: null, HeadquartersSymbol: null, Credits: 175_000, StartingFaction: "COSMIC", ShipCount: 2));

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);

        port.GetMyContractsAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ContractModel>([
                new ContractModel(
                    Id: "C-ACC-1",
                    FactionSymbol: "COSMIC",
                    Type: "PROCUREMENT",
                    IsAccepted: false,
                    IsFulfilled: false,
                    Expiration: DateTimeOffset.UtcNow.AddDays(3),
                    DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                    TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                    Deliverables: [new ContractDeliverableModel("IRON_ORE", "X1-AB-MKT", 20, 0)])
            ], 1, 1, 20));

        var notAccepted = new List<ContractDto>
        {
            new(
                Id: "C-ACC-1",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: false,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("IRON_ORE", "X1-AB-MKT", 20, 0),
                }))
        };

        var accepted = new List<ContractDto>
        {
            new(
                Id: "C-ACC-1",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("IRON_ORE", "X1-AB-MKT", 20, 0),
                }))
        };

        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(notAccepted, accepted);

        port.AcceptContractAsync("C-ACC-1", Arg.Any<CancellationToken>())
            .Returns(new ContractActionResult(
                ContractId: "C-ACC-1",
                FactionSymbol: "COSMIC",
                ContractType: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                Deliverables: [new ContractDeliverableModel("IRON_ORE", "X1-AB-MKT", 20, 0)],
                AgentSymbol: "AGENT",
                AgentCredits: 176_136,
                ShipCargo: null)
            {
                PaymentOnAccepted = 1_136,
            });

        var idleMiner = new ShipModel(
            Symbol: "SHIP-MINER-1",
            SystemSymbol: "X1-AB",
            WaypointSymbol: "X1-AB-START",
            Status: "DOCKED",
            FlightMode: "CRUISE",
            FuelCurrent: 100,
            FuelCapacity: 100,
            CargoCurrent: 0,
            CargoCapacity: 40,
            ShipType: "SHIP_MINING_DRONE",
            MountSymbols: ["MOUNT_MINING_LASER_I"]);

        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([idleMiner]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var waypoints = Substitute.For<IWaypointRepository>();
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-AST", "X1-AB", "ASTEROID_FIELD", 0, 0, false, false, DateTimeOffset.UtcNow, TraitsJson: "IRON_ORE")
        ]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            Substitute.For<IShipyardRepository>(),
            waypoints,
            port,
            Substitute.For<IShipPurchaseService>(),
            agents,
            bus,
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            log.For<ContractPlanService>());

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        // The journal (slice 2.3): the contract accepted, then the plan started.
        log.Journal.Select(e => e.EventKind).Should().Equal("ContractAccepted", "PlanStarted");
        log.Journal[0].Message.Should().Be("ContractAccepted: contract C-ACC-1 accepted (IRON_ORE to X1-AB-MKT); it paid 1136 credits.");
        await port.Received(1).AcceptContractAsync("C-ACC-1", Arg.Any<CancellationToken>());
        await agents.Received(1).UpsertAsync(Arg.Is<AgentModel>(a => a.Credits == 176_136), Arg.Any<CancellationToken>());

        // B7: and the ledger and the metrics hear of it.
        await bus.Received(1).PublishAsync(
            Arg.Is<ContractAcceptedEvent>(e => e.ContractId == "C-ACC-1" && e.Payment == 1_136),
            Arg.Any<DeliveryOptions>());
        await bus.Received(1).PublishAsync(
            Arg.Is<AgentCreditsChangedEvent>(e => e.OldCredits == 175_000 && e.NewCredits == 176_136),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task AdvanceAsync_UpdatesPlanProgress_WhenDeliverableStillPending()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-ADV-1",
            ShipSymbol = "SHIP-MINER-1",
            TradeSymbol = "IRON_ORE",
            SourceWaypoint = "X1-AB-AST",
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 60,
            UnitsFulfilled = 10,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });

        contracts.FindAsync("C-ADV-1", Arg.Any<CancellationToken>()).Returns(new ContractDto(
            Id: "C-ADV-1",
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: false,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
            {
                new("IRON_ORE", "X1-AB-MKT", 60, 35),
            })));

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.AdvanceAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.Active
                && p.ContractId == "C-ADV-1"
                && p.UnitsRequired == 60
                && p.UnitsFulfilled == 35),
            Arg.Any<CancellationToken>());

        await assignments.DidNotReceive().UpsertAsync(Arg.Any<ShipAssignmentDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_KeepsThePlanActive_UntilTheContractIsFulfilled()
    {
        // Every unit delivered isn't the end yet: the fulfil call, which pays, still has to go out,
        // and the ship's assignment is what sends the ship there to make it (B9). Completing the plan
        // here would leave a contract unfulfilled whenever that call failed.
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-ADV-2",
            ShipSymbol = "SHIP-MINER-2",
            TradeSymbol = "COPPER_ORE",
            SourceWaypoint = "X1-AB-AST",
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 40,
            UnitsFulfilled = 20,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });

        contracts.FindAsync("C-ADV-2", Arg.Any<CancellationToken>()).Returns(new ContractDto(
            Id: "C-ADV-2",
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: false,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
            {
                new("COPPER_ORE", "X1-AB-MKT", 40, 40),
            })));

        var assignment = new ShipAssignmentDto(
            ShipSymbol: "SHIP-MINER-2",
            AssignmentType: "Contract",
            OriginWaypoint: "X1-AB-AST",
            DestWaypoint: "X1-AB-MKT",
            CargoSymbol: "COPPER_ORE",
            ContractId: "C-ADV-2",
            StepIndex: 0,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-8),
            CompletedAt: null,
            PurchaseUnitPrice: 0,
            RequiredUnits: 40,
            SupplyCompleted: false);
        assignments.FindAsync("SHIP-MINER-2", Arg.Any<CancellationToken>()).Returns(assignment);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([assignment]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.AdvanceAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.Active
                && p.ContractId == "C-ADV-2"
                && p.UnitsRequired == 40
                && p.UnitsFulfilled == 40),
            Arg.Any<CancellationToken>());

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-MINER-2"
                && a.AssignmentType == "Contract"
                && a.ContractId == "C-ADV-2"
                && !a.CompletedAt.HasValue
                && a.RequiredUnits == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_CompletesTheActivePlanOnceItsContractIsFulfilled_AndReleasesTheShip()
    {
        // B9: the plan only advanced on two events that nothing publishes, so after fulfilment it
        // stayed Active and kept the ship's assignment open, and the tick went on sending the ship to
        // mine and deliver for a fulfilled contract. The tick now advances it from the cached contract.
        var (sut, plans, assignments) = ActivePlanFor42Units(fulfilled: true, unitsFulfilled: 42, assignmentRequiredUnits: 3);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p => p.Status == ContractMineralPlanStatus.Completed && p.ContractId == "C-B9"),
            Arg.Any<CancellationToken>());
        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a => a.ContractId == "C-B9" && a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_RecordsDeliveriesOnTheActivePlan()
    {
        var (sut, plans, assignments) = ActivePlanFor42Units(fulfilled: false, unitsFulfilled: 25, assignmentRequiredUnits: 32);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p => p.Status == ContractMineralPlanStatus.Active && p.UnitsFulfilled == 25),
            Arg.Any<CancellationToken>());
        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a => a.RequiredUnits == 17 && !a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_OnAnActivePlanWithNoNewDeliveries_WritesNothing()
    {
        // This runs on every tick.
        var (sut, plans, assignments) = ActivePlanFor42Units(fulfilled: false, unitsFulfilled: 10, assignmentRequiredUnits: 32);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await plans.DidNotReceive().UpsertAsync(Arg.Any<ContractMineralPlanState>(), Arg.Any<CancellationToken>());
        await assignments.DidNotReceive().UpsertAsync(Arg.Any<ShipAssignmentDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_UpdatesAssignmentRequiredUnits_ToRemainingDeliverableUnits()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-ADV-3",
            ShipSymbol = "SHIP-MINER-3",
            TradeSymbol = "IRON_ORE",
            SourceWaypoint = "X1-AB-AST",
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 80,
            UnitsFulfilled = 20,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });

        contracts.FindAsync("C-ADV-3", Arg.Any<CancellationToken>()).Returns(new ContractDto(
            Id: "C-ADV-3",
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: false,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
            {
                new("IRON_ORE", "X1-AB-MKT", 80, 50),
            })));

        var assignment = new ShipAssignmentDto(
            ShipSymbol: "SHIP-MINER-3",
            AssignmentType: "Contract",
            OriginWaypoint: "X1-AB-AST",
            DestWaypoint: "X1-AB-MKT",
            CargoSymbol: "IRON_ORE",
            ContractId: "C-ADV-3",
            StepIndex: 0,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-12),
            CompletedAt: null,
            PurchaseUnitPrice: 0,
            RequiredUnits: 80,
            SupplyCompleted: false);
        assignments.FindAsync("SHIP-MINER-3", Arg.Any<CancellationToken>()).Returns(assignment);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([assignment]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.AdvanceAsync(CancellationToken.None);

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-MINER-3"
                && a.AssignmentType == "Contract"
                && a.ContractId == "C-ADV-3"
                && !a.CompletedAt.HasValue
                && a.RequiredUnits == 30),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_CompletesPlanAndAssignment_WhenContractAlreadyFulfilled()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-ADV-4",
            ShipSymbol = "SHIP-MINER-4",
            TradeSymbol = "COPPER_ORE",
            SourceWaypoint = "X1-AB-AST",
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 30,
            UnitsFulfilled = 25,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });

        contracts.FindAsync("C-ADV-4", Arg.Any<CancellationToken>()).Returns(new ContractDto(
            Id: "C-ADV-4",
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: true,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
            {
                new("COPPER_ORE", "X1-AB-MKT", 30, 30),
            })));

        var assignment = new ShipAssignmentDto(
            ShipSymbol: "SHIP-MINER-4",
            AssignmentType: "Contract",
            OriginWaypoint: "X1-AB-AST",
            DestWaypoint: "X1-AB-MKT",
            CargoSymbol: "COPPER_ORE",
            ContractId: "C-ADV-4",
            StepIndex: 0,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-12),
            CompletedAt: null,
            PurchaseUnitPrice: 0,
            RequiredUnits: 5,
            SupplyCompleted: false);
        assignments.FindAsync("SHIP-MINER-4", Arg.Any<CancellationToken>()).Returns(assignment);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([assignment]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.AdvanceAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.Completed
                && p.ContractId == "C-ADV-4"
                && p.UnitsFulfilled >= p.UnitsRequired),
            Arg.Any<CancellationToken>());

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-MINER-4"
                && a.AssignmentType == "Contract"
                && a.ContractId == "C-ADV-4"
                && a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_Retries_WhenExistingPlanIsPendingBudget()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-PB-1",
            ShipSymbol = string.Empty,
            TradeSymbol = "IRON_ORE",
            SourceWaypoint = string.Empty,
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 25,
            UnitsFulfilled = 0,
            Status = ContractMineralPlanStatus.PendingBudget,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });

        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await contracts.Received(2).GetActiveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeliverableObtainedEvent_AdvancesPlan_WhenEventMatchesActivePlan()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-EVT-1",
            ShipSymbol = "SHIP-MINER-1",
            TradeSymbol = "COPPER_ORE",
            SourceWaypoint = "X1-AB-AST",
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 40,
            UnitsFulfilled = 10,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });

        contracts.FindAsync("C-EVT-1", Arg.Any<CancellationToken>()).Returns(new ContractDto(
            Id: "C-EVT-1",
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: false,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
            {
                new("COPPER_ORE", "X1-AB-MKT", 40, 12),
            })));
        var evnt = new DeliverableObtainedEvent("SHIP-MINER-1", "COPPER_ORE", 2);
        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.Handle(evnt, CancellationToken.None);

        await plans.Received().UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.ContractId == "C-EVT-1"
                && p.Status == ContractMineralPlanStatus.Active
                && p.UnitsFulfilled == 12),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeliverableObtainedEvent_DoesNotAdvance_WhenEventTradeSymbolDiffers()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-EVT-2",
            ShipSymbol = "SHIP-MINER-1",
            TradeSymbol = "COPPER_ORE",
            SourceWaypoint = "X1-AB-AST",
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 40,
            UnitsFulfilled = 10,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.Handle(new DeliverableObtainedEvent("SHIP-MINER-1", "IRON_ORE", 4), CancellationToken.None);

        await contracts.DidNotReceive().FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await plans.DidNotReceive().UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p => p.ContractId == "C-EVT-2" && p.UnitsFulfilled != 10),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_PrefersNearestAsteroid_OverTraitScoredFarTarget()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto(
                Id: "C-NEAR-1",
                FactionSymbol: "COSMIC",
                Type: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
                {
                    new("COPPER_ORE", "X1-AB-MKT", 30, 0),
                }))
        ]);

        var idleMiner = new ShipModel(
            Symbol: "SHIP-MINER-N",
            SystemSymbol: "X1-AB",
            WaypointSymbol: "X1-AB-J59",
            Status: "DOCKED",
            FlightMode: "CRUISE",
            FuelCurrent: 100,
            FuelCapacity: 100,
            CargoCurrent: 0,
            CargoCapacity: 40,
            ShipType: "SHIP_MINING_DRONE",
            MountSymbols: ["MOUNT_MINING_LASER_I"]);

        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([idleMiner]);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-J59", "X1-AB", "ASTEROID_BASE", -201, 694, true, false, DateTimeOffset.UtcNow),
            new WaypointCacheModel("X1-AB-B10", "X1-AB", "ASTEROID", -372, -88, false, false, DateTimeOffset.UtcNow, TraitsJson: "COPPER_ORE"),
            new WaypointCacheModel("X1-AB-J68", "X1-AB", "ASTEROID", -165, 744, false, false, DateTimeOffset.UtcNow),
            new WaypointCacheModel("X1-AB-MKT", "X1-AB", "PLANET", -201, 694, true, false, DateTimeOffset.UtcNow)
        ]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            Substitute.For<IShipyardRepository>(),
            waypoints,
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.Active
                && p.ContractId == "C-NEAR-1"
                && p.SourceWaypoint == "X1-AB-J59"),
            Arg.Any<CancellationToken>());
    }

    // An Active plan for 42 units, 10 of them delivered when it last advanced, and the contract as
    // cached now.
    private static (ContractPlanService Sut, IContractMineralPlanRepository Plans, IShipAssignmentRepository Assignments) ActivePlanFor42Units(
        bool fulfilled,
        int unitsFulfilled,
        int assignmentRequiredUnits)
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-B9",
            ShipSymbol = "SHIP-MINER-9",
            TradeSymbol = "IRON_ORE",
            SourceWaypoint = "X1-AB-AST",
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 42,
            UnitsFulfilled = 10,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        contracts.FindAsync("C-B9", Arg.Any<CancellationToken>()).Returns(new ContractDto(
            Id: "C-B9",
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: fulfilled,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("IRON_ORE", "X1-AB-MKT", 42, unitsFulfilled) })));
        var assignment = new ShipAssignmentDto(
            ShipSymbol: "SHIP-MINER-9",
            AssignmentType: "Contract",
            OriginWaypoint: "X1-AB-AST",
            DestWaypoint: "X1-AB-MKT",
            CargoSymbol: "IRON_ORE",
            ContractId: "C-B9",
            StepIndex: 0,
            AssignedAt: DateTimeOffset.UtcNow.AddHours(-2),
            CompletedAt: null,
            PurchaseUnitPrice: 0,
            RequiredUnits: assignmentRequiredUnits,
            SupplyCompleted: false);
        assignments.FindAsync("SHIP-MINER-9", Arg.Any<CancellationToken>()).Returns(assignment);
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([assignment]);
        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<IShipPurchaseService>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IShipGoalRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IPlanRepository>(),
            new OpenPurchaseOrder(),
            NullLogger<ContractPlanService>.Instance);
        return (sut, plans, assignments);
    }
}
