using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class ScoutAllMarketplacesPlanServiceTests
{
    [Fact]
    public async Task EnsureBootstrappedAsync_CreatesPlanAndFirstAssignment_WhenNoPlanExists()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var shipSelection = Substitute.For<IScoutShipSelectionService>();
        var discovery = Substitute.For<IScoutMarketplaceDiscoveryService>();
        var routePlanner = Substitute.For<IMarketplaceRoutePlanner>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>()).Returns((ScoutAllMarketplacesPlanState?)null);

        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-START", "DOCKED", "CRUISE", 100, 100);
        shipSelection.SelectAsync(Arg.Any<CancellationToken>()).Returns(ship);

        var markets = new List<WaypointCacheModel>
        {
            Market("X1-AB-001"),
            Market("X1-AB-002"),
        };
        discovery.DiscoverAsync(ship, Arg.Any<CancellationToken>()).Returns(markets);
        routePlanner.BuildRouteAsync("X1-AB-START", markets, Arg.Any<CancellationToken>())
            .Returns(["X1-AB-001", "X1-AB-002"]);

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto?)null);

        var sut = new ScoutAllMarketplacesPlanService(
            scoutPlans,
            shipSelection,
            discovery,
            routePlanner,
            assignments,
            Substitute.For<IShipGoalRepository>(),
            NullLogger<ScoutAllMarketplacesPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await scoutPlans.Received(1).UpsertAsync(
            Arg.Is<ScoutAllMarketplacesPlanState>(p =>
                p.ShipSymbol == "SHIP-1"
                && p.StartWaypointSymbol == "X1-AB-START"
                && p.CurrentRouteIndex == 0
                && p.Status == ScoutPlanStatus.Active
                && p.RouteWaypointSymbols.SequenceEqual(new[] { "X1-AB-001", "X1-AB-002" })),
            Arg.Any<CancellationToken>());

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-1"
                && a.AssignmentType == "Scout"
                && a.DestWaypoint == "X1-AB-001"
                && a.StepIndex == 0
                && !a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_DoesNothing_WhenPlanAlreadyExists()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var shipSelection = Substitute.For<IScoutShipSelectionService>();
        var discovery = Substitute.For<IScoutMarketplaceDiscoveryService>();
        var routePlanner = Substitute.For<IMarketplaceRoutePlanner>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        var plan = new ScoutAllMarketplacesPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = "SHIP-1",
            StartWaypointSymbol = "X1-AB-START",
            RouteWaypointSymbols = ["X1-AB-001"],
            CurrentRouteIndex = 0,
            Status = ScoutPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        scoutPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);

        // Correct active assignment already exists for the current step.
        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(
            new ShipAssignmentDto(
                ShipSymbol: "SHIP-1",
                AssignmentType: "Scout",
                OriginWaypoint: null,
                DestWaypoint: "X1-AB-001",
                CargoSymbol: null,
                ContractId: null,
                StepIndex: 0,
                AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAt: null));

        var sut = new ScoutAllMarketplacesPlanService(
            scoutPlans,
            shipSelection,
            discovery,
            routePlanner,
            assignments,
            Substitute.For<IShipGoalRepository>(),
            NullLogger<ScoutAllMarketplacesPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await shipSelection.DidNotReceiveWithAnyArgs().SelectAsync(default);
        await scoutPlans.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
        await assignments.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_DoesNotCreateAssignment_WhenActiveAssignmentExists()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var shipSelection = Substitute.For<IScoutShipSelectionService>();
        var discovery = Substitute.For<IScoutMarketplaceDiscoveryService>();
        var routePlanner = Substitute.For<IMarketplaceRoutePlanner>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>()).Returns((ScoutAllMarketplacesPlanState?)null);

        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-START", "DOCKED", "CRUISE", 100, 100);
        shipSelection.SelectAsync(Arg.Any<CancellationToken>()).Returns(ship);

        var markets = new List<WaypointCacheModel> { Market("X1-AB-001") };
        discovery.DiscoverAsync(ship, Arg.Any<CancellationToken>()).Returns(markets);
        routePlanner.BuildRouteAsync("X1-AB-START", markets, Arg.Any<CancellationToken>()).Returns(["X1-AB-001"]);

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(new ShipAssignmentDto(
            ShipSymbol: "SHIP-1",
            AssignmentType: "Scout",
            OriginWaypoint: null,
            DestWaypoint: "X1-AB-999",
            CargoSymbol: null,
            ContractId: null,
            StepIndex: 5,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt: null));

        var sut = new ScoutAllMarketplacesPlanService(
            scoutPlans,
            shipSelection,
            discovery,
            routePlanner,
            assignments,
            Substitute.For<IShipGoalRepository>(),
            NullLogger<ScoutAllMarketplacesPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await scoutPlans.Received(1).UpsertAsync(Arg.Any<ScoutAllMarketplacesPlanState>(), Arg.Any<CancellationToken>());
        await assignments.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    private static WaypointCacheModel Market(string symbol) =>
        new(
            Symbol: symbol,
            SystemSymbol: "X1-AB",
            Type: "PLANET",
            X: 0,
            Y: 0,
            HasMarket: true,
            HasShipyard: false,
            LastObservedAt: DateTimeOffset.UtcNow);

    private static ScoutAllMarketplacesPlanService CreateService(
        IScoutPlanRepository scoutPlans,
        IShipAssignmentRepository assignments) =>
        new(
            scoutPlans,
            Substitute.For<IScoutShipSelectionService>(),
            Substitute.For<IScoutMarketplaceDiscoveryService>(),
            Substitute.For<IMarketplaceRoutePlanner>(),
            assignments,
            Substitute.For<IShipGoalRepository>(),
            NullLogger<ScoutAllMarketplacesPlanService>.Instance);

    private static ScoutAllMarketplacesPlanState ActivePlan(string shipSymbol, IReadOnlyList<string> route, int currentIndex = 0) =>
        new()
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = shipSymbol,
            StartWaypointSymbol = route[0],
            RouteWaypointSymbols = route,
            CurrentRouteIndex = currentIndex,
            Status = ScoutPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static ScoutAllMarketplacesPlanState CompletedPlan(string shipSymbol, IReadOnlyList<string> route) =>
        new()
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = shipSymbol,
            StartWaypointSymbol = route[0],
            RouteWaypointSymbols = route,
            CurrentRouteIndex = route.Count - 1,
            Status = ScoutPlanStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    // ──────────────────────────────────────────────────────────────────────────────
    // Slice 4.6: AdvanceAsync
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceAsync_CreatesNextAssignment_WhenMoreWaypointsRemain()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002", "X1-AB-003"], currentIndex: 0));

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        await scoutPlans.Received(1).UpsertAsync(
            Arg.Is<ScoutAllMarketplacesPlanState>(p =>
                p.CurrentRouteIndex == 1 && p.Status == ScoutPlanStatus.Active),
            Arg.Any<CancellationToken>());

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-1"
                && a.AssignmentType == "Scout"
                && a.DestWaypoint == "X1-AB-002"
                && a.StepIndex == 1
                && !a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_MarksplanComplete_WhenLastWaypointReached()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002"], currentIndex: 1));

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        await scoutPlans.Received(1).UpsertAsync(
            Arg.Is<ScoutAllMarketplacesPlanState>(p => p.Status == ScoutPlanStatus.Completed),
            Arg.Any<CancellationToken>());

        await assignments.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task AdvanceAsync_DoesNothing_WhenNoPlanExists()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>()).Returns((ScoutAllMarketplacesPlanState?)null);

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        await scoutPlans.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
        await assignments.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task AdvanceAsync_DoesNothing_WhenPlanBelongsToDifferentShip()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("OTHER-SHIP", ["X1-AB-001", "X1-AB-002"], currentIndex: 0));

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        await scoutPlans.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
        await assignments.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Slice 5.1: AdvanceAsync marks current assignment complete
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceAsync_MarksCurrentAssignmentComplete_WhenMoreWaypointsRemain()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002"], currentIndex: 0));

        var activeAssignment = new ShipAssignmentDto(
            ShipSymbol: "SHIP-1",
            AssignmentType: "Scout",
            OriginWaypoint: null,
            DestWaypoint: "X1-AB-001",
            CargoSymbol: null,
            ContractId: null,
            StepIndex: 0,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt: null);

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(activeAssignment);

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-1"
                && a.DestWaypoint == "X1-AB-001"
                && a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_MarksCurrentAssignmentComplete_WhenLastWaypointReached()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002"], currentIndex: 1));

        var activeAssignment = new ShipAssignmentDto(
            ShipSymbol: "SHIP-1",
            AssignmentType: "Scout",
            OriginWaypoint: null,
            DestWaypoint: "X1-AB-002",
            CargoSymbol: null,
            ContractId: null,
            StepIndex: 1,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt: null);

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(activeAssignment);

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-1"
                && a.DestWaypoint == "X1-AB-002"
                && a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_DoesNotMarkComplete_WhenAssignmentAlreadyCompleted()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002"], currentIndex: 0));

        var alreadyCompleted = new ShipAssignmentDto(
            ShipSymbol: "SHIP-1",
            AssignmentType: "Scout",
            OriginWaypoint: null,
            DestWaypoint: "X1-AB-001",
            CargoSymbol: null,
            ContractId: null,
            StepIndex: 0,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(alreadyCompleted);

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        // Should NOT upsert the already-completed assignment again.
        await assignments.DidNotReceive().UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a => a.DestWaypoint == "X1-AB-001"),
            Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Slice 5.2: Next waypoint is read from plan state, not recomputed
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceAsync_NextDestination_DerivedFromPersistedRoute_NotRecomputed()
    {
        // The route planner is intentionally not configured here.
        // AdvanceAsync must never call it; it must read from RouteWaypointSymbols.
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var routePlanner = Substitute.For<IMarketplaceRoutePlanner>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002", "X1-AB-003"], currentIndex: 1));

        var sut = new ScoutAllMarketplacesPlanService(
            scoutPlans,
            Substitute.For<IScoutShipSelectionService>(),
            Substitute.For<IScoutMarketplaceDiscoveryService>(),
            routePlanner,
            assignments,
            Substitute.For<IShipGoalRepository>(),
            NullLogger<ScoutAllMarketplacesPlanService>.Instance);

        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        // Next assignment must target the third waypoint in the persisted route.
        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.DestWaypoint == "X1-AB-003"
                && a.StepIndex == 2),
            Arg.Any<CancellationToken>());

        // Route planner must never be called during advancement.
        await routePlanner.DidNotReceiveWithAnyArgs()
            .BuildRouteAsync(default!, default!, default);
    }

    [Fact]
    public async Task AdvanceAsync_CorrectlySequences_AllWaypointsFromPersistedRoute()
    {
        // Simulate sequential advances through a four-waypoint route.
        // Each advance must derive its destination from plan.RouteWaypointSymbols.
        var route = new[] { "X1-AA-001", "X1-AA-002", "X1-AA-003", "X1-AA-004" };

        for (var startIndex = 0; startIndex < route.Length - 1; startIndex++)
        {
            var scoutPlans = Substitute.For<IScoutPlanRepository>();
            var assignments = Substitute.For<IShipAssignmentRepository>();

            scoutPlans.GetAsync(Arg.Any<CancellationToken>())
                .Returns(ActivePlan("SHIP-1", route, currentIndex: startIndex));

            var sut = CreateService(scoutPlans, assignments);
            await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

            var expectedNext = route[startIndex + 1];
            await assignments.Received(1).UpsertAsync(
                Arg.Is<ShipAssignmentDto>(a =>
                    a.DestWaypoint == expectedNext
                    && a.StepIndex == startIndex + 1),
                Arg.Any<CancellationToken>());
        }
    }
    // ──────────────────────────────────────────────────────────────────────────────
    // Slice 5.3: Next assignment created only if none is active
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceAsync_DoesNotCreateNextAssignment_WhenNextStepAlreadyActive()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002", "X1-AB-003"], currentIndex: 0));

        // Simulate that step 1 (the next step) is already an active assignment.
        var nextStepAlreadyActive = new ShipAssignmentDto(
            ShipSymbol: "SHIP-1",
            AssignmentType: "Scout",
            OriginWaypoint: null,
            DestWaypoint: "X1-AB-002",
            CargoSymbol: null,
            ContractId: null,
            StepIndex: 1,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt: null);

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(nextStepAlreadyActive);

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        // Must not upsert any new assignment.
        await assignments.DidNotReceive().UpsertAsync(Arg.Any<ShipAssignmentDto>(), Arg.Any<CancellationToken>());
        // Must not advance the plan.
        await scoutPlans.DidNotReceive().UpsertAsync(Arg.Any<ScoutAllMarketplacesPlanState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_DoesNotCreateDuplicateAssignment_WhenCalledTwice()
    {
        // First advance: plan at index 0, assignment for step 0 is active.
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002"], currentIndex: 0));

        var activeAtStep0 = new ShipAssignmentDto(
            ShipSymbol: "SHIP-1",
            AssignmentType: "Scout",
            OriginWaypoint: null,
            DestWaypoint: "X1-AB-001",
            CargoSymbol: null,
            ContractId: null,
            StepIndex: 0,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt: null);

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(activeAtStep0);

        var sut = CreateService(scoutPlans, assignments);

        // First call: should complete step 0 and create step 1.
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a => a.StepIndex == 1 && !a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());

        // Now simulate the state after first advance: plan at index 1, assignment for step 1 active.
        scoutPlans.ClearReceivedCalls();
        assignments.ClearReceivedCalls();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002"], currentIndex: 1));

        var activeAtStep1 = new ShipAssignmentDto(
            ShipSymbol: "SHIP-1",
            AssignmentType: "Scout",
            OriginWaypoint: null,
            DestWaypoint: "X1-AB-002",
            CargoSymbol: null,
            ContractId: null,
            StepIndex: 1,
            AssignedAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAt: null);

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(activeAtStep1);

        // Second (duplicate) advance call: plan already advanced; should be blocked.
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        // nextIndex for currentIndex=1 would be 2, which is out of range (plan complete path).
        // But regardless, no NEW non-completed assignment should be written for step 2.
        await assignments.DidNotReceive().UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a => !a.CompletedAt.HasValue && a.StepIndex == 2),
            Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Slice 5.4: Plan completes cleanly; no new work after final waypoint
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceAsync_PersistsCompletedStatus_WhenLastWaypointAdvanced()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        var route = new[] { "X1-AB-001", "X1-AB-002", "X1-AB-003" };
        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", route, currentIndex: 2));

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        await scoutPlans.Received(1).UpsertAsync(
            Arg.Is<ScoutAllMarketplacesPlanState>(p =>
                p.Status == ScoutPlanStatus.Completed
                && p.ShipSymbol == "SHIP-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_DoesNotCreateAssignment_WhenLastWaypointAdvanced()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        var route = new[] { "X1-AB-001", "X1-AB-002", "X1-AB-003" };
        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", route, currentIndex: 2));

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        await assignments.DidNotReceive().UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a => !a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_IsNoOp_WhenPlanIsAlreadyCompleted()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(CompletedPlan("SHIP-1", ["X1-AB-001", "X1-AB-002"]));

        var sut = CreateService(scoutPlans, assignments);
        await sut.AdvanceAsync("SHIP-1", CancellationToken.None);

        await scoutPlans.DidNotReceive().UpsertAsync(Arg.Any<ScoutAllMarketplacesPlanState>(), Arg.Any<CancellationToken>());
        await assignments.DidNotReceive().UpsertAsync(Arg.Any<ShipAssignmentDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_DoesNotRestartCompletedPlan()
    {
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var shipSelection = Substitute.For<IScoutShipSelectionService>();
        var discovery = Substitute.For<IScoutMarketplaceDiscoveryService>();
        var routePlanner = Substitute.For<IMarketplaceRoutePlanner>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(CompletedPlan("SHIP-1", ["X1-AB-001", "X1-AB-002"]));

        var sut = new ScoutAllMarketplacesPlanService(
            scoutPlans,
            shipSelection,
            discovery,
            routePlanner,
            assignments,
            Substitute.For<IShipGoalRepository>(),
            NullLogger<ScoutAllMarketplacesPlanService>.Instance);

        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        // Bootstrap must bail out: no ship selection, no discovery, no route planning, no new plan.
        await shipSelection.DidNotReceiveWithAnyArgs().SelectAsync(default);
        await discovery.DidNotReceiveWithAnyArgs().DiscoverAsync(default!, default);
        await routePlanner.DidNotReceiveWithAnyArgs().BuildRouteAsync(default!, default!, default);
        await scoutPlans.DidNotReceive().UpsertAsync(Arg.Any<ScoutAllMarketplacesPlanState>(), Arg.Any<CancellationToken>());
        await assignments.DidNotReceive().UpsertAsync(Arg.Any<ShipAssignmentDto>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Slice 5.5: Restart safety — resume from persisted plan and assignment state
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureBootstrappedAsync_ReCreatesAssignment_WhenPlanActiveButNoAssignmentExists()
    {
        // Simulates a crash after plan was persisted but before the first assignment was written.
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002", "X1-AB-003"], currentIndex: 0));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto?)null);

        var sut = CreateService(scoutPlans, assignments);
        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-1"
                && a.AssignmentType == "Scout"
                && a.DestWaypoint == "X1-AB-001"
                && a.StepIndex == 0
                && !a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_ReCreatesAssignment_WhenPlanAdvancedButNextAssignmentMissing()
    {
        // Simulates a crash after plan index was advanced to 2 but before the step-2 assignment was written.
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002", "X1-AB-003"], currentIndex: 2));

        // Only the completed step-1 assignment is present; step-2 was never written.
        var completedPreviousAssignment = new ShipAssignmentDto(
            ShipSymbol: "SHIP-1",
            AssignmentType: "Scout",
            OriginWaypoint: null,
            DestWaypoint: "X1-AB-002",
            CargoSymbol: null,
            ContractId: null,
            StepIndex: 1,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(completedPreviousAssignment);

        var sut = CreateService(scoutPlans, assignments);
        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a =>
                a.ShipSymbol == "SHIP-1"
                && a.DestWaypoint == "X1-AB-003"
                && a.StepIndex == 2
                && !a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_DoesNotReCreateAssignment_WhenCorrectActiveAssignmentExists()
    {
        // Normal running state: plan at index 1 and step-1 assignment is already active.
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ActivePlan("SHIP-1", ["X1-AB-001", "X1-AB-002", "X1-AB-003"], currentIndex: 1));

        var correctActiveAssignment = new ShipAssignmentDto(
            ShipSymbol: "SHIP-1",
            AssignmentType: "Scout",
            OriginWaypoint: null,
            DestWaypoint: "X1-AB-002",
            CargoSymbol: null,
            ContractId: null,
            StepIndex: 1,
            AssignedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt: null);

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(correctActiveAssignment);

        var sut = CreateService(scoutPlans, assignments);
        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await assignments.DidNotReceive().UpsertAsync(Arg.Any<ShipAssignmentDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_IsNoOp_WhenPlanIsCompleted()
    {
        // Completed plan: no assignment recreation should occur even if assignments are missing.
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();

        scoutPlans.GetAsync(Arg.Any<CancellationToken>())
            .Returns(CompletedPlan("SHIP-1", ["X1-AB-001", "X1-AB-002"]));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto?)null);

        var sut = CreateService(scoutPlans, assignments);
        await sut.EnsureBootstrappedAsync(CancellationToken.None);

        await assignments.DidNotReceive().UpsertAsync(Arg.Any<ShipAssignmentDto>(), Arg.Any<CancellationToken>());
    }
}

