using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;

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
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
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
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            waypoints,
            Substitute.For<ISpaceTradersPort>(),
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
    public async Task EnsureBootstrappedAsync_CreatesPendingBudgetPlan_WhenNoMinerAndCannotPurchase()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();

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

        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");
        shipyards.FindByWaypointAsync("X1-AB-SHIPYARD", Arg.Any<CancellationToken>()).Returns(new ShipyardWaypointDto
        {
            WaypointSymbol = "X1-AB-SHIPYARD",
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_MINING_DRONE"],
            Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 100_000 }],
        });

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            agents,
            shipyards,
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
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

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await contracts.DidNotReceive().GetActiveAsync(Arg.Any<CancellationToken>());
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

        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");
        shipyards.FindByWaypointAsync("X1-AB-SHIPYARD", Arg.Any<CancellationToken>()).Returns(new ShipyardWaypointDto
        {
            WaypointSymbol = "X1-AB-SHIPYARD",
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_MINING_DRONE"],
            Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 100_000 }],
        });

        port.PurchaseShipAsync("SHIP_MINING_DRONE", "X1-AB-SHIPYARD", Arg.Any<CancellationToken>())
            .Returns(new PurchaseShipActionResult(
                Agent: new AgentModel("AGENT", null, null, 400_000, "COSMIC", 2),
                ShipSymbol: "SHIP-NEW-MINER",
                ShipNav: new NavModel("DOCKED", "X1-AB", "X1-AB-SHIPYARD", "CRUISE", null, null),
                ShipFuel: new FuelModel(80, 100),
                Cost: 100_000));

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-AST", "X1-AB", "ASTEROID_FIELD", 0, 0, false, false, DateTimeOffset.UtcNow, TraitsJson: "IRON_ORE"),
            new WaypointCacheModel("X1-AB-MKT", "X1-AB", "PLANET", 1, 1, true, false, DateTimeOffset.UtcNow)
        ]);

        var sut = new ContractPlanService(
            plans,
            contracts,
            ships,
            assignments,
            agents,
            shipyards,
            waypoints,
            port,
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await port.Received(1).PurchaseShipAsync("SHIP_MINING_DRONE", "X1-AB-SHIPYARD", Arg.Any<CancellationToken>());
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
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            waypoints,
            port,
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
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            waypoints,
            port,
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await port.Received(1).NegotiateContractAsync("SHIP-NEGOTIATOR", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_AcceptsContract_WhenPendingContractNotAccepted()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var port = Substitute.For<ISpaceTradersPort>();

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
                AgentSymbol: null,
                AgentCredits: null,
                ShipCargo: null));

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
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            waypoints,
            port,
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await port.Received(1).AcceptContractAsync("C-ACC-1", Arg.Any<CancellationToken>());
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
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
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
    public async Task AdvanceAsync_CompletesPlanAndAssignment_WhenDeliverableIsSatisfied()
    {
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

        assignments.FindAsync("SHIP-MINER-2", Arg.Any<CancellationToken>()).Returns(new ShipAssignmentDto(
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
            SupplyCompleted: false));

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            NullLogger<ContractPlanService>.Instance);

        await sut.AdvanceAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.Completed
                && p.ContractId == "C-ADV-2"
                && p.UnitsRequired == 40
                && p.UnitsFulfilled == 40),
            Arg.Any<CancellationToken>());

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-MINER-2"
                && a.AssignmentType == "Contract"
                && a.ContractId == "C-ADV-2"
                && a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
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

        assignments.FindAsync("SHIP-MINER-3", Arg.Any<CancellationToken>()).Returns(new ShipAssignmentDto(
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
            SupplyCompleted: false));

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
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

        assignments.FindAsync("SHIP-MINER-4", Arg.Any<CancellationToken>()).Returns(new ShipAssignmentDto(
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
            SupplyCompleted: false));

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
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
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
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
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
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
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
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
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            waypoints,
            Substitute.For<ISpaceTradersPort>(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await plans.Received(1).UpsertAsync(
            Arg.Is<ContractMineralPlanState>(p =>
                p.Status == ContractMineralPlanStatus.Active
                && p.ContractId == "C-NEAR-1"
                && p.SourceWaypoint == "X1-AB-J59"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_RestoresAssignment_WhenActivePlanExistsButAssignmentMissing()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-RESTORE-1",
            ShipSymbol = "SHIP-MINER-1",
            TradeSymbol = "COPPER_ORE",
            SourceWaypoint = "X1-AB-AST",
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 60,
            UnitsFulfilled = 10,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });

        contracts.FindAsync("C-RESTORE-1", Arg.Any<CancellationToken>()).Returns(new ContractDto(
            Id: "C-RESTORE-1",
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: false,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
            {
                new("COPPER_ORE", "X1-AB-MKT", 60, 10),
            })));

        assignments.FindAsync("SHIP-MINER-1", Arg.Any<CancellationToken>())
            .Returns((ShipAssignmentDto?)null);

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-MINER-1"
                && a.AssignmentType == "Contract"
                && a.ContractId == "C-RESTORE-1"
                && a.CargoSymbol == "COPPER_ORE"
                && a.OriginWaypoint == "X1-AB-AST"
                && a.DestWaypoint == "X1-AB-MKT"
                && !a.CompletedAt.HasValue
                && a.RequiredUnits == 50),
            Arg.Any<CancellationToken>());

        await contracts.DidNotReceive().GetActiveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_ReplacesNonContractAssignment_WhenActivePlanExists()
    {
        var plans = Substitute.For<IContractMineralPlanRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        plans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-RESTORE-2",
            ShipSymbol = "SHIP-MINER-2",
            TradeSymbol = "IRON_ORE",
            SourceWaypoint = "X1-AB-AST",
            DestinationWaypoint = "X1-AB-MKT",
            UnitsRequired = 40,
            UnitsFulfilled = 5,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });

        contracts.FindAsync("C-RESTORE-2", Arg.Any<CancellationToken>()).Returns(new ContractDto(
            Id: "C-RESTORE-2",
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: false,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
            {
                new("IRON_ORE", "X1-AB-MKT", 40, 5),
            })));

        assignments.FindAsync("SHIP-MINER-2", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto(
                ShipSymbol: "SHIP-MINER-2",
                AssignmentType: "Scout",
                OriginWaypoint: "X1-AB-S1",
                DestWaypoint: null,
                CargoSymbol: null,
                ContractId: null,
                StepIndex: 0,
                AssignedAt: DateTimeOffset.UtcNow.AddHours(-1),
                CompletedAt: null,
                PurchaseUnitPrice: 0,
                RequiredUnits: 0,
                SupplyCompleted: false));

        var sut = new ContractPlanService(
            plans,
            contracts,
            Substitute.For<IShipRepository>(),
            assignments,
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipyardRepository>(),
            Substitute.For<IWaypointRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            NullLogger<ContractPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-MINER-2"
                && a.AssignmentType == "Contract"
                && a.ContractId == "C-RESTORE-2"
                && a.CargoSymbol == "IRON_ORE"
                && a.OriginWaypoint == "X1-AB-AST"
                && a.DestWaypoint == "X1-AB-MKT"
                && a.RequiredUnits == 35),
            Arg.Any<CancellationToken>());
    }
}
