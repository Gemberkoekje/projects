using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Services;

public sealed class FleetStatusQueryServiceTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IFleetGoalRepository GoalRepo(params FleetGoal[] goals)
    {
        var repo = Substitute.For<IFleetGoalRepository>();
        repo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(goals);
        return repo;
    }

    private static IShipRepository NoShips()
    {
        var repo = Substitute.For<IShipRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        return repo;
    }

    private static IShipRepository ShipsWith(params string[] symbols)
    {
        var repo = Substitute.For<IShipRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            symbols.Select(s => new ShipModel(s, "X1", "X1-A", "DOCKED", "CRUISE", 100, 100)).ToArray());
        return repo;
    }

    private static IShipGoalRepository NoShipGoals()
    {
        var repo = Substitute.For<IShipGoalRepository>();
        repo.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipGoal?)null);
        return repo;
    }

    private static IShipGoalRepository ShipGoalsWith(params (string shipSymbol, ShipGoal goal)[] assignments)
    {
        var repo = Substitute.For<IShipGoalRepository>();
        repo.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipGoal?)null);
        foreach (var (symbol, goal) in assignments)
        {
            repo.GetActiveGoalAsync(symbol, Arg.Any<CancellationToken>()).Returns(goal);
        }

        return repo;
    }

    private static IContractRepository NoContracts()
    {
        var repo = Substitute.For<IContractRepository>();
        repo.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ContractDto?)null);
        return repo;
    }

    private static IContractRepository ContractWith(
        string contractId,
        params ContractDeliverableDto[] deliverables)
    {
        var repo = Substitute.For<IContractRepository>();
        repo.FindAsync(contractId, Arg.Any<CancellationToken>()).Returns(
            new ContractDto(contractId, "COSMIC", "PROCURE", true, false, null, null,
                DeliverablesJson: JsonSerializer.Serialize(deliverables)));
        return repo;
    }

    private static IConstructionRepository NoConstruction()
    {
        var repo = Substitute.For<IConstructionRepository>();
        repo.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ConstructionSiteModel?)null);
        return repo;
    }

    private static IConstructionRepository ConstructionSiteWith(
        string waypointSymbol,
        params ConstructionMaterialModel[] materials)
    {
        var repo = Substitute.For<IConstructionRepository>();
        repo.FindAsync(waypointSymbol, Arg.Any<CancellationToken>()).Returns(
            new ConstructionSiteModel(waypointSymbol, false, materials));
        return repo;
    }

    private static IShipAssignmentRepository NoAssignments()
    {
        var repo = Substitute.For<IShipAssignmentRepository>();
        repo.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto?)null);
        return repo;
    }

    private static IContractMineralPlanRepository NoContractPlans()
    {
        var repo = Substitute.For<IContractMineralPlanRepository>();
        repo.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        return repo;
    }

    private static FleetStatusQueryService Build(
        IFleetGoalRepository? goalRepo = null,
        IShipRepository? shipRepo = null,
        IShipGoalRepository? shipGoalRepo = null,
        IShipAssignmentRepository? assignmentRepo = null,
        IContractRepository? contractRepo = null,
        IConstructionRepository? constructionRepo = null,
        IContractMineralPlanRepository? contractPlanRepo = null)
    {
        var historyRepo = Substitute.For<IShipGoalHistoryRepository>();
        historyRepo.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var ledger = Substitute.For<ILedgerRepository>();
        ledger.GetShipSummaryAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ShipLedgerWindowSummaryDto(0, 0, 0, 0));
        return new(
            goalRepo ?? GoalRepo(),
            shipRepo ?? NoShips(),
            shipGoalRepo ?? NoShipGoals(),
            assignmentRepo ?? NoAssignments(),
            contractRepo ?? NoContracts(),
            constructionRepo ?? NoConstruction(),
            historyRepo,
            contractPlanRepo ?? NoContractPlans(),
            ledger);
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetGoalChainsAsync_ReturnsEmpty_WhenNoActiveGoals()
    {
        var svc = Build(goalRepo: GoalRepo());
        var result = await svc.GetGoalChainsAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetShipGoalHistoryAsync_EnrichesEntriesWithLedgerSummary()
    {
        var shipRepo = Substitute.For<IShipRepository>();
        shipRepo.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100));

        var startedAt = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var endedAt = startedAt.AddMinutes(20);
        var historyEntry = new ShipGoalHistoryEntry
        {
            Id = Guid.NewGuid(),
            ShipSymbol = "SHIP-1",
            GoalKind = "MineResource",
            GoalId = Guid.NewGuid(),
            Outcome = "Completed",
            StartedAt = startedAt,
            EndedAt = endedAt,
        };

        var historyRepo = Substitute.For<IShipGoalHistoryRepository>();
        historyRepo.GetRecentAsync("SHIP-1", 5, Arg.Any<CancellationToken>())
            .Returns([historyEntry]);

        var ledger = Substitute.For<ILedgerRepository>();
        ledger.GetShipSummaryAsync("SHIP-1", startedAt, endedAt, Arg.Any<CancellationToken>())
            .Returns(new ShipLedgerWindowSummaryDto(900, 250, 650, 4));

        var svc = new FleetStatusQueryService(
            GoalRepo(),
            shipRepo,
            NoShipGoals(),
            NoAssignments(),
            NoContracts(),
            NoConstruction(),
            historyRepo,
            NoContractPlans(),
            ledger);

        var result = await svc.GetShipGoalHistoryAsync("SHIP-1", 5);

        result.Should().NotBeNull();
        result.Should().ContainSingle();
        result![0].CreditsEarned.Should().Be(900);
        result[0].CreditsSpent.Should().Be(250);
        result[0].NetCredits.Should().Be(650);
        result[0].LedgerEntryCount.Should().Be(4);
        result[0].DurationSeconds.Should().Be(1200);
    }

    [Fact]
    public async Task GetGoalChainsAsync_SingleConstructionGoal_TwoResourceNeeds_PartialDelivery()
    {
        // Two FleetGoals for the same construction site.
        var goal1 = new FleetGoal(
            FleetGoalKind.Construction,
            "Supply FAB_MATS",
            Priority: 30,
            TradeSymbol: "FAB_MATS",
            DestinationWaypoint: "X1-AB-GATE",
            RemainingUnits: 800);

        var goal2 = new FleetGoal(
            FleetGoalKind.Construction,
            "Supply ADVANCED_CIRCUITRY",
            Priority: 30,
            TradeSymbol: "ADVANCED_CIRCUITRY",
            DestinationWaypoint: "X1-AB-GATE",
            RemainingUnits: 300);

        var constructionRepo = ConstructionSiteWith("X1-AB-GATE",
            new ConstructionMaterialModel("FAB_MATS", 1000, 200),
            new ConstructionMaterialModel("ADVANCED_CIRCUITRY", 500, 200));

        var svc = Build(
            goalRepo: GoalRepo(goal1, goal2),
            constructionRepo: constructionRepo);

        var result = await svc.GetGoalChainsAsync();

        result.Should().HaveCount(1);
        var chain = result[0];
        chain.FleetGoalKind.Should().Be(FleetGoalKind.Construction);
        chain.Priority.Should().Be(30);
        chain.FleetGoalDescription.Should().Contain("X1-AB-GATE");

        chain.ResourceNeeds.Should().HaveCount(2);

        var fabricated = chain.ResourceNeeds.Single(r => r.TradeSymbol == "FAB_MATS");
        fabricated.UnitsNeeded.Should().Be(1000);
        fabricated.UnitsDelivered.Should().Be(200);
        fabricated.AssignedShips.Should().BeEmpty();

        var circuits = chain.ResourceNeeds.Single(r => r.TradeSymbol == "ADVANCED_CIRCUITRY");
        circuits.UnitsNeeded.Should().Be(500);
        circuits.UnitsDelivered.Should().Be(200);
    }

    [Fact]
    public async Task GetGoalChainsAsync_ContractGoal_AllDeliveryComplete_StillActive()
    {
        // The deliverable is fully fulfilled, but the fleet goal is still active
        // (contract not yet accepted/completed).
        var goal = new FleetGoal(
            FleetGoalKind.Contract,
            "Deliver IRON_ORE",
            Priority: 20,
            ContractId: "c1",
            TradeSymbol: "IRON_ORE",
            RemainingUnits: 0);

        var contractRepo = ContractWith("c1",
            new ContractDeliverableDto("IRON_ORE", "X1-DEST", 100, 100));

        var svc = Build(
            goalRepo: GoalRepo(goal),
            contractRepo: contractRepo);

        var result = await svc.GetGoalChainsAsync();

        result.Should().HaveCount(1);
        var chain = result[0];
        chain.FleetGoalKind.Should().Be(FleetGoalKind.Contract);

        chain.ResourceNeeds.Should().HaveCount(1);
        var need = chain.ResourceNeeds[0];
        need.UnitsNeeded.Should().Be(100);
        need.UnitsDelivered.Should().Be(100, "all units have been delivered");
        need.AssignedShips.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGoalChainsAsync_GoalWithNoAssignedShips()
    {
        var goal = new FleetGoal(
            FleetGoalKind.Construction,
            "Supply BAUXITE",
            Priority: 30,
            TradeSymbol: "BAUXITE",
            DestinationWaypoint: "X1-CD-GATE",
            RemainingUnits: 500);

        var svc = Build(
            goalRepo: GoalRepo(goal),
            shipRepo: ShipsWith("SHIP-1", "SHIP-2"),
            shipGoalRepo: NoShipGoals());

        var result = await svc.GetGoalChainsAsync();

        result.Should().HaveCount(1);
        result[0].ResourceNeeds[0].AssignedShips.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGoalChainsAsync_MixedList_SortedByPriorityAscending()
    {
        var contractGoal = new FleetGoal(
            FleetGoalKind.Contract,
            "Deliver IRON_ORE",
            Priority: 20,
            ContractId: "c2",
            TradeSymbol: "IRON_ORE",
            RemainingUnits: 50);

        var scoutingGoal = new FleetGoal(
            FleetGoalKind.MarketScouting,
            "Scout X1-A1",
            Priority: 10,
            OriginWaypoint: "X1-A1-MARKET");

        var svc = Build(
            goalRepo: GoalRepo(contractGoal, scoutingGoal),
            contractRepo: ContractWith("c2",
                new ContractDeliverableDto("IRON_ORE", "X1-DEST", 100, 50)));

        var result = await svc.GetGoalChainsAsync();

        result.Should().HaveCount(2);
        result[0].Priority.Should().Be(10, "MarketScouting has lower priority value and should come first");
        result[0].FleetGoalKind.Should().Be(FleetGoalKind.MarketScouting);
        result[1].Priority.Should().Be(20);
        result[1].FleetGoalKind.Should().Be(FleetGoalKind.Contract);
    }

    [Fact]
    public async Task GetGoalChainsAsync_ContractGoal_PopulatesAssignedShips()
    {
        var goal = new FleetGoal(
            FleetGoalKind.Contract,
            "Deliver COPPER",
            Priority: 20,
            ContractId: "c3",
            TradeSymbol: "COPPER_ORE",
            RemainingUnits: 60);

        var shipGoalRepo = ShipGoalsWith(
            ("HAULER-1", new DeliverCargoGoal
            {
                ContractId = "c3",
                TradeSymbol = "COPPER_ORE",
                DeliveryWaypointSymbol = "X1-DEST",
            }),
            ("HAULER-2", new DeliverCargoGoal
            {
                ContractId = "c3",
                TradeSymbol = "COPPER_ORE",
                DeliveryWaypointSymbol = "X1-DEST",
            }));

        var svc = Build(
            goalRepo: GoalRepo(goal),
            shipRepo: ShipsWith("HAULER-1", "HAULER-2"),
            shipGoalRepo: shipGoalRepo,
            contractRepo: ContractWith("c3",
                new ContractDeliverableDto("COPPER_ORE", "X1-DEST", 120, 60)));

        var result = await svc.GetGoalChainsAsync();

        result.Should().HaveCount(1);
        var need = result[0].ResourceNeeds[0];
        need.AssignedShips.Should().BeEquivalentTo(new[] { "HAULER-1", "HAULER-2" });
    }

    [Fact]
    public async Task GetGoalChainsAsync_NonResourceGoals_HaveEmptyResourceNeeds()
    {
        var goals = new[]
        {
            new FleetGoal(FleetGoalKind.MarketScouting, "Scout market", 10, OriginWaypoint: "X1-A1-MARKET"),
            new FleetGoal(FleetGoalKind.MarketCoverage, "Cover market", 40, OriginWaypoint: "X1-B2-MARKET"),
            new FleetGoal(FleetGoalKind.FleetExpansion, "Buy miner", 50,
                ExpansionShipType: "SHIP_MINING_DRONE",
                ExpansionShipyardWaypoint: "X1-AB-SHIPYARD"),
        };

        var svc = Build(goalRepo: GoalRepo(goals));

        var result = await svc.GetGoalChainsAsync();

        result.Should().HaveCount(3);
        result.Should().AllSatisfy(chain => chain.ResourceNeeds.Should().BeEmpty());
    }

    [Fact]
    public async Task GetGoalChainsAsync_NoFleetGoals_FallsBackToActiveContractPlan()
    {
        var contractPlans = Substitute.For<IContractMineralPlanRepository>();
        contractPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "c-fallback",
            ShipSymbol = "MINER-1",
            TradeSymbol = "COPPER_ORE",
            SourceWaypoint = "X1-ASTEROID",
            DestinationWaypoint = "X1-DEST",
            UnitsRequired = 120,
            UnitsFulfilled = 40,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            StopReason = null,
        });

        var svc = Build(
            goalRepo: GoalRepo(),
            contractPlanRepo: contractPlans,
            contractRepo: ContractWith("c-fallback",
                new ContractDeliverableDto("COPPER_ORE", "X1-DEST", 120, 40)));

        var result = await svc.GetGoalChainsAsync();

        result.Should().HaveCount(1);
        result[0].FleetGoalKind.Should().Be(FleetGoalKind.Contract);
        result[0].FleetGoalDescription.Should().Be("Contract c-fallback");
        result[0].ResourceNeeds.Should().ContainSingle();
        result[0].ResourceNeeds[0].AssignedShips.Should().ContainSingle("MINER-1");
    }
}

// ---------------------------------------------------------------------------
// Phase 15d tests: GetAssignmentsAsync
// ---------------------------------------------------------------------------

public sealed class FleetStatusQueryServiceAssignmentTests
{
    private static IFleetGoalRepository GoalRepo(params FleetGoal[] goals)
    {
        var repo = Substitute.For<IFleetGoalRepository>();
        repo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(goals);
        return repo;
    }

    private static IShipRepository ShipsWith(params string[] symbols)
    {
        var repo = Substitute.For<IShipRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            symbols.Select(s => new ShipModel(s, "X1", "X1-A", "DOCKED", "CRUISE", 100, 100)).ToArray());
        return repo;
    }

    private static IShipGoalRepository ShipGoalsWith(params (string symbol, ShipGoal goal)[] assignments)
    {
        var repo = Substitute.For<IShipGoalRepository>();
        repo.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipGoal?)null);
        foreach (var (symbol, goal) in assignments)
        {
            repo.GetActiveGoalAsync(symbol, Arg.Any<CancellationToken>()).Returns(goal);
        }
        return repo;
    }

    private static IShipAssignmentRepository AssignmentsWith(params (string symbol, DateTimeOffset at)[] entries)
    {
        var repo = Substitute.For<IShipAssignmentRepository>();
        repo.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto?)null);
        foreach (var (symbol, at) in entries)
        {
            repo.FindAsync(symbol, Arg.Any<CancellationToken>()).Returns(
                new ShipAssignmentDto(symbol, "Test", null, null, null, null, 0, at, null));
        }
        return repo;
    }

    private static IContractMineralPlanRepository NoContractPlans()
    {
        var repo = Substitute.For<IContractMineralPlanRepository>();
        repo.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);
        return repo;
    }

    private static FleetStatusQueryService Build(
        IFleetGoalRepository? goalRepo = null,
        IShipRepository? shipRepo = null,
        IShipGoalRepository? shipGoalRepo = null,
        IShipAssignmentRepository? assignmentRepo = null,
        IContractRepository? contractRepo = null,
        IConstructionRepository? constructionRepo = null,
        IContractMineralPlanRepository? contractPlanRepo = null)
    {
        var contracts = Substitute.For<IContractRepository>();
        contracts.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ContractDto?)null);

        var constructions = Substitute.For<IConstructionRepository>();
        constructions.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ConstructionSiteModel?)null);

        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        assignments.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto?)null);

        var historyRepo = Substitute.For<IShipGoalHistoryRepository>();
        historyRepo.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var ledger = Substitute.For<ILedgerRepository>();
        ledger.GetShipSummaryAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ShipLedgerWindowSummaryDto(0, 0, 0, 0));

        return new(
            goalRepo ?? GoalRepo(),
            shipRepo ?? ShipsWith(),
            shipGoalRepo ?? Substitute.For<IShipGoalRepository>(),
            assignmentRepo ?? assignments,
            contractRepo ?? contracts,
            constructionRepo ?? constructions,
            historyRepo,
            contractPlanRepo ?? NoContractPlans(),
            ledger);
    }

    [Fact]
    public async Task GetAssignmentsAsync_ReturnsEmpty_WhenNoShips()
    {
        var svc = Build(shipRepo: ShipsWith());
        var result = await svc.GetAssignmentsAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAssignmentsAsync_ShipWithMineResourceGoal_LinkedToConstructionFleetGoal()
    {
        var assignedAt = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fleetGoal = new FleetGoal(
            FleetGoalKind.Construction,
            "Supply BAUXITE to X1-AB-GATE",
            Priority: 30,
            TradeSymbol: "BAUXITE",
            DestinationWaypoint: "X1-AB-GATE",
            RemainingUnits: 500);

        var mineGoal = new MineResourceGoal
        {
            TradeSymbol = "BAUXITE",
            SourceWaypointSymbol = "X1-AB-ASTEROID",
        };

        var svc = Build(
            goalRepo: GoalRepo(fleetGoal),
            shipRepo: ShipsWith("MINER-1"),
            shipGoalRepo: ShipGoalsWith(("MINER-1", mineGoal)),
            assignmentRepo: AssignmentsWith(("MINER-1", assignedAt)));

        var result = await svc.GetAssignmentsAsync();

        result.Should().HaveCount(1);
        var snap = result[0];
        snap.ShipSymbol.Should().Be("MINER-1");
        snap.GoalKind.Should().Be(ShipGoalKind.MineResource);
        snap.GoalDescription.Should().Contain("BAUXITE").And.Contain("X1-AB-ASTEROID");
        snap.AssignedAt.Should().Be(assignedAt);
        snap.FleetGoalId.Should().Be(fleetGoal.Id);
        snap.FleetGoalDescription.Should().Be(fleetGoal.Description);
    }

    [Fact]
    public async Task GetAssignmentsAsync_ShipWithIdleGoal_NoFleetGoalLink()
    {
        var svc = Build(
            shipRepo: ShipsWith("SHIP-1"),
            shipGoalRepo: ShipGoalsWith(("SHIP-1", new IdleGoal())));

        var result = await svc.GetAssignmentsAsync();

        result.Should().HaveCount(1);
        result[0].GoalKind.Should().Be(ShipGoalKind.Idle);
        result[0].FleetGoalId.Should().BeNull();
        result[0].FleetGoalDescription.Should().BeNull();
    }

    [Fact]
    public async Task GetAssignmentsAsync_ShipWithNoGoalSet_TreatedAsIdle()
    {
        var shipGoalRepo = Substitute.For<IShipGoalRepository>();
        shipGoalRepo.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipGoal?)null);

        var svc = Build(
            shipRepo: ShipsWith("SHIP-1"),
            shipGoalRepo: shipGoalRepo);

        var result = await svc.GetAssignmentsAsync();

        result.Should().HaveCount(1);
        result[0].GoalKind.Should().Be(ShipGoalKind.Idle);
        result[0].GoalDescription.Should().Be("Idle");
    }

    [Fact]
    public async Task GetAssignmentsAsync_MultipleShips_DifferentGoalKinds()
    {
        var svc = Build(
            goalRepo: GoalRepo(),
            shipRepo: ShipsWith("HAULER-1", "MINER-1", "SCOUT-1"),
            shipGoalRepo: ShipGoalsWith(
                ("HAULER-1", new DeliverCargoGoal { ContractId = "c1", TradeSymbol = "IRON_ORE", DeliveryWaypointSymbol = "X1-DEST" }),
                ("MINER-1", new MineResourceGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-ASTEROID" }),
                ("SCOUT-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-MARKET" })));

        var result = await svc.GetAssignmentsAsync();

        result.Should().HaveCount(3);
        result.Select(s => s.ShipSymbol).Should().BeEquivalentTo(["HAULER-1", "MINER-1", "SCOUT-1"]);
        result.Should().ContainSingle(s => s.GoalKind == ShipGoalKind.DeliverCargo);
        result.Should().ContainSingle(s => s.GoalKind == ShipGoalKind.MineResource);
        result.Should().ContainSingle(s => s.GoalKind == ShipGoalKind.ScoutWaypoint);
    }

    [Fact]
    public async Task GetAssignmentsAsync_MineAndSellGoal_UsesMiningSnapshot()
    {
        var svc = Build(
            shipRepo: ShipsWith("MINER-1"),
            shipGoalRepo: ShipGoalsWith(
                ("MINER-1", new MineAndSellGoal
                {
                    TradeSymbol = "COPPER_ORE",
                    SourceWaypointSymbol = "X1-PT96-B10",
                    SellWaypointSymbol = "X1-PT96-H52",
                })));

        var result = await svc.GetAssignmentsAsync();

        result.Should().HaveCount(1);
        result[0].GoalKind.Should().Be(ShipGoalKind.MineResource);
        result[0].GoalDescription.Should().Contain("Mining COPPER_ORE").And.Contain("X1-PT96-B10").And.Contain("X1-PT96-H52");
        result[0].SourceWaypoint.Should().Be("X1-PT96-B10");
        result[0].DestinationWaypoint.Should().Be("X1-PT96-H52");
    }

    [Fact]
    public async Task GetAssignmentsAsync_TradeBetweenMarketsGoal_UsesTradingSnapshot()
    {
        var svc = Build(
            shipRepo: ShipsWith("TRADER-1"),
            shipGoalRepo: ShipGoalsWith(
                ("TRADER-1", new TradeBetweenMarketsGoal
                {
                    TradeSymbol = "SHIP_PARTS",
                    BuyWaypointSymbol = "X1-PT96-D41",
                    SellWaypointSymbol = "X1-PT96-H53",
                })));

        var result = await svc.GetAssignmentsAsync();

        result.Should().HaveCount(1);
        result[0].GoalKind.Should().Be(ShipGoalKind.SellCargo);
        result[0].GoalDescription.Should().Contain("Trading SHIP_PARTS").And.Contain("X1-PT96-D41").And.Contain("X1-PT96-H53");
        result[0].SourceWaypoint.Should().Be("X1-PT96-D41");
        result[0].DestinationWaypoint.Should().Be("X1-PT96-H53");
    }

    [Fact]
    public async Task GetAssignmentsAsync_NoShipGoal_UsesActiveContractAssignmentSnapshot()
    {
        var assignedAt = new DateTimeOffset(2024, 2, 1, 10, 0, 0, TimeSpan.Zero);

        var assignmentRepo = Substitute.For<IShipAssignmentRepository>();
        assignmentRepo.FindAsync("MINER-1", Arg.Any<CancellationToken>()).Returns(
            new ShipAssignmentDto(
                "MINER-1",
                "Contract",
                "X1-ASTEROID",
                "X1-DEST",
                "COPPER_ORE",
                "c4",
                0,
                assignedAt,
                null));

        var shipGoalRepo = Substitute.For<IShipGoalRepository>();
        shipGoalRepo.GetActiveGoalAsync("MINER-1", Arg.Any<CancellationToken>()).Returns((ShipGoal?)null);

        var contractPlans = Substitute.For<IContractMineralPlanRepository>();
        var planId = Guid.NewGuid();
        contractPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = planId,
            ContractId = "c4",
            ShipSymbol = "MINER-1",
            TradeSymbol = "COPPER_ORE",
            SourceWaypoint = "X1-ASTEROID",
            DestinationWaypoint = "X1-DEST",
            UnitsRequired = 100,
            UnitsFulfilled = 20,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = assignedAt,
            UpdatedAt = assignedAt,
            StopReason = null,
        });

        var svc = Build(
            shipRepo: ShipsWith("MINER-1"),
            shipGoalRepo: shipGoalRepo,
            assignmentRepo: assignmentRepo,
            contractPlanRepo: contractPlans);

        var result = await svc.GetAssignmentsAsync();

        result.Should().HaveCount(1);
        result[0].GoalKind.Should().Be(ShipGoalKind.MineResource);
        result[0].GoalDescription.Should().Contain("Mining COPPER_ORE");
        result[0].FleetGoalId.Should().Be(planId);
        result[0].FleetGoalDescription.Should().Be("Contract c4");
    }
}

// ---------------------------------------------------------------------------
// Phase 15f tests: GetShipActivitiesAsync and GetShipActivityAsync
// ---------------------------------------------------------------------------

public sealed class FleetStatusQueryServiceActivityTests
{
    private static DateTimeOffset Future(int seconds) =>
        DateTimeOffset.UtcNow.AddSeconds(seconds);

    private static IShipRepository ShipWith(ShipModel ship)
    {
        var repo = Substitute.For<IShipRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([ship]);
        repo.FindAsync(ship.Symbol, Arg.Any<CancellationToken>()).Returns(ship);
        return repo;
    }

    private static IShipGoalRepository GoalFor(string shipSymbol, ShipGoal goal)
    {
        var repo = Substitute.For<IShipGoalRepository>();
        repo.GetActiveGoalAsync(shipSymbol, Arg.Any<CancellationToken>()).Returns(goal);
        return repo;
    }

    private static FleetStatusQueryService Build(
        IShipRepository shipRepo,
        IShipGoalRepository? shipGoalRepo = null)
    {
        var goalRepo = Substitute.For<IFleetGoalRepository>();
        goalRepo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto?)null);

        var contracts = Substitute.For<IContractRepository>();
        contracts.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ContractDto?)null);

        var constructions = Substitute.For<IConstructionRepository>();
        constructions.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ConstructionSiteModel?)null);

        var defaultGoalRepo = Substitute.For<IShipGoalRepository>();
        defaultGoalRepo.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipGoal?)null);

        var historyRepo = Substitute.For<IShipGoalHistoryRepository>();
        historyRepo.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var contractPlans = Substitute.For<IContractMineralPlanRepository>();
        contractPlans.GetAsync(Arg.Any<CancellationToken>()).Returns((ContractMineralPlanState?)null);

        var ledger = Substitute.For<ILedgerRepository>();
        ledger.GetShipSummaryAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ShipLedgerWindowSummaryDto(0, 0, 0, 0));

        return new(goalRepo, shipRepo, shipGoalRepo ?? defaultGoalRepo, assignments, contracts, constructions, historyRepo, contractPlans, ledger);
    }

    [Fact]
    public async Task GetShipActivitiesAsync_InTransit_DescriptionContainsDestAndArrival()
    {
        var arrives = Future(3600);
        var ship = new ShipModel("SHIP-1", "X1", "X1-A",
            Status: "IN_TRANSIT",
            FlightMode: "CRUISE",
            FuelCurrent: 80, FuelCapacity: 100,
            ArrivesAt: arrives,
            DestWaypointSymbol: "X1-B",
            CargoCurrent: 10, CargoCapacity: 40);

        var svc = Build(ShipWith(ship));
        var result = await svc.GetShipActivitiesAsync();

        result.Should().HaveCount(1);
        var snap = result[0];
        snap.LocalStatus.Should().Be(ShipLocalStatus.InTransit);
        snap.ActivityDescription.Should().Contain("Moving to X1-B").And.Contain("arrives");
        snap.DestinationWaypoint.Should().Be("X1-B");
        snap.EstimatedArrival.Should().Be(arrives);
        snap.CurrentWaypoint.Should().BeNull();
    }

    [Fact]
    public async Task GetShipActivitiesAsync_InOrbit_OnCooldown_MiningGoal()
    {
        var cooldownExpires = Future(120);
        var ship = new ShipModel("SHIP-1", "X1", "X1-ASTEROID",
            Status: "IN_ORBIT",
            FlightMode: "CRUISE",
            FuelCurrent: 60, FuelCapacity: 100,
            CooldownExpiresAt: cooldownExpires);

        var goalRepo = GoalFor("SHIP-1", new MineResourceGoal
        {
            TradeSymbol = "BAUXITE",
            SourceWaypointSymbol = "X1-ASTEROID",
        });

        var svc = Build(ShipWith(ship), goalRepo);
        var result = await svc.GetShipActivitiesAsync();

        result.Should().HaveCount(1);
        var snap = result[0];
        snap.OnCooldown.Should().BeTrue();
        snap.ActivityDescription.Should().Contain("Extracting BAUXITE").And.Contain("cooldown");
    }

    [Fact]
    public async Task GetShipActivitiesAsync_InOrbit_ReadyToExtract()
    {
        var ship = new ShipModel("SHIP-1", "X1", "X1-ASTEROID",
            Status: "IN_ORBIT",
            FlightMode: "CRUISE",
            FuelCurrent: 60, FuelCapacity: 100);

        var goalRepo = GoalFor("SHIP-1", new MineResourceGoal
        {
            TradeSymbol = "BAUXITE",
            SourceWaypointSymbol = "X1-ASTEROID",
        });

        var svc = Build(ShipWith(ship), goalRepo);
        var result = await svc.GetShipActivitiesAsync();

        result.Should().HaveCount(1);
        var snap = result[0];
        snap.OnCooldown.Should().BeFalse();
        snap.ActivityDescription.Should().Contain("In orbit").And.Contain("ready to extract");
    }

    [Fact]
    public async Task GetShipActivitiesAsync_Docked_SellingCargo()
    {
        var ship = new ShipModel("SHIP-1", "X1", "X1-MARKET",
            Status: "DOCKED",
            FlightMode: "CRUISE",
            FuelCurrent: 60, FuelCapacity: 100,
            CargoCurrent: 30, CargoCapacity: 40);

        var goalRepo = GoalFor("SHIP-1", new SellCargoGoal
        {
            DestinationWaypointSymbol = "X1-MARKET",
            TradeSymbols = ["BAUXITE"],
        });

        var svc = Build(ShipWith(ship), goalRepo);
        var result = await svc.GetShipActivitiesAsync();

        result.Should().HaveCount(1);
        result[0].ActivityDescription.Should().Contain("Docked").And.Contain("X1-MARKET").And.Contain("selling cargo");
    }

    [Fact]
    public async Task GetShipActivityAsync_ReturnsNull_ForUnknownShipSymbol()
    {
        var emptyRepo = Substitute.For<IShipRepository>();
        emptyRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        emptyRepo.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipModel?)null);

        var svc = Build(emptyRepo);
        var result = await svc.GetShipActivityAsync("UNKNOWN-1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetShipActivityAsync_Idle_ReturnsIdleDescription()
    {
        var ship = new ShipModel("SHIP-1", "X1", "X1-BASE",
            Status: "DOCKED",
            FlightMode: "CRUISE",
            FuelCurrent: 100, FuelCapacity: 100);

        var svc = Build(ShipWith(ship));
        var result = await svc.GetShipActivityAsync("SHIP-1");

        result.Should().NotBeNull();
        result!.ActivityDescription.Should().Contain("Docked").And.Contain("X1-BASE");
    }
}
