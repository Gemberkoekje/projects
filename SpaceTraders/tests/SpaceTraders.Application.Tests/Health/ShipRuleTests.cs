using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Tests.Roles;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Health;

/// <summary>Phase 3.2: a ship with a goal changes state within N minutes, unless it's in transit.</summary>
public sealed class ShipStuckRuleTests
{
    private static readonly DateTimeOffset Start = RuleHarness.Start;

    private readonly FleetFixture _fleet = new();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly RuleHarness _harness = new();
    private readonly ShipStuckRule _rule;

    public ShipStuckRuleTests()
    {
        _rule = new ShipStuckRule(_fleet.Fleet, _settings);
    }

    [Fact]
    public async Task AShipWithAGoal_ThatTheBotDoesntUpdateFor30Minutes_IsStuck()
    {
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start.AddMinutes(-5)));
        _fleet.Give("SHIP-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2" });

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(30))).Should().BeEmpty();

        // It has to look stuck at two evaluations in a row.
        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(31))).Should().BeEmpty();
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(32));

        var violation = violations.Should().ContainSingle().Which;
        violation.Subject.Should().Be("SHIP-1");
        violation.Details.Should().Be(
            "the bot hasn't updated the ship (nav, cargo, fuel or cooldown) for 32 minutes, since 2026-10-01 12:00:00Z, though it has a ScoutWaypoint goal; it is IN_ORBIT at X1-AB-A1; limit 30 minutes (Health.Ship.MaxMinutesWithoutChange)");
    }

    [Fact]
    public async Task AShipThatJustArrived_IsNotStuck_WhileTheArrivalHandlerDocksIt()
    {
        // The fleet is loaded with arrivals dead-reckoned, which drops the arrival time: for a moment
        // a ship that arrived from a 44-minute trip looks unchanged since it left.
        _fleet.Give("SHIP-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2" });
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start, arrivesAt: Start.AddMinutes(45)));
        await _harness.EvaluateAsync(_rule, Start);

        _fleet.Have(FleetFixture.Drone("SHIP-1", Start.AddMinutes(1)));
        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(45))).Should().BeEmpty();

        _fleet.Have(FleetFixture.Drone("SHIP-1", Start.AddMinutes(45)));
        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(46))).Should().BeEmpty();
    }

    [Fact]
    public async Task AWorkingShip_IsUpdatedAllTheTime()
    {
        // In the soak test a contract drone sat in orbit for up to 69 minutes per trip, extracting
        // every 71 seconds: each extraction updates its cargo and cooldown.
        _fleet.Have(FleetFixture.Drone("SHIP-3", Start.AddMinutes(59)));
        _fleet.Assign(FleetFixture.ContractAssignment("SHIP-3", "C-1"));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(60))).Should().BeEmpty();
    }

    [Fact]
    public async Task AContractAssignment_IsWorkToo()
    {
        _fleet.Have(FleetFixture.Drone("SHIP-3", Start));
        _fleet.Assign(FleetFixture.ContractAssignment("SHIP-3", "C-1"));

        await _harness.EvaluateAsync(_rule, Start);
        await _harness.EvaluateAsync(_rule, Start.AddMinutes(31));
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(32));

        violations.Should().ContainSingle().Which.Details.Should().Contain("though it has a Contract assignment");
    }

    [Fact]
    public async Task AShipInTransit_IsNotStuck()
    {
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start, arrivesAt: Start.AddHours(2)));
        _fleet.Give("SHIP-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2" });

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(90))).Should().BeEmpty();
    }

    [Fact]
    public async Task TheClock_StartsNoEarlierThanTheArrival()
    {
        // The bot last updated the ship when it left; the arrival handler docks it a moment after.
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start, arrivesAt: Start.AddMinutes(40)));
        _fleet.Give("SHIP-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2" });

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(41))).Should().BeEmpty();
    }

    [Fact]
    public async Task AShipWithoutWork_IsNotStuck()
    {
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start.AddHours(-5)));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(2))).Should().BeEmpty();
    }

    [Fact]
    public async Task ABlockedGoal_IsTheCircuitBreakersRule()
    {
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start));
        _fleet.Give("SHIP-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2", Status = GoalStatus.Blocked, StatusReason = "runaway" });

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(2))).Should().BeEmpty();
    }

    [Fact]
    public async Task AShipWhosePlanIsOff_Waits_OnPurpose()
    {
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start));
        _fleet.Give("SHIP-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2" });
        _harness.PlansOn.Remove(AutomationPlan.Scout);

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(2))).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLimit_IsASetting()
    {
        _settings.GetAsync<int>(ShipStuckRule.Setting, Arg.Any<CancellationToken>()).Returns(5);
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start));
        _fleet.Give("SHIP-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2" });

        await _harness.EvaluateAsync(_rule, Start);
        await _harness.EvaluateAsync(_rule, Start.AddMinutes(6));

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(7))).Should().ContainSingle();
    }
}

/// <summary>Phase 3.2 with D13: a ship isn't left idle for more than N minutes while a plan has work for it.</summary>
public sealed class ShipLeftIdleRuleTests
{
    private static readonly DateTimeOffset Start = RuleHarness.Start;

    private readonly FleetFixture _fleet = new();
    private readonly IScoutPlanRepository _scout = Substitute.For<IScoutPlanRepository>();
    private readonly ContractFixture _contract = new();
    private readonly IProbeDeploymentPlanRepository _probes = Substitute.For<IProbeDeploymentPlanRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly RuleHarness _harness = new();
    private readonly ShipLeftIdleRule _rule;

    public ShipLeftIdleRuleTests()
    {
        _rule = new ShipLeftIdleRule(_fleet.Fleet, _scout, _contract.Plans, _probes, _plans, _settings);
    }

    [Fact]
    public async Task TheFirstRun_LeavesShipsIdleByDesign()
    {
        // D9, D1: scouting done, the one contract fulfilled, probes, mining and trading off.
        _harness.PlansOn.ExceptWith([AutomationPlan.ProbeDeployment, AutomationPlan.Mining, AutomationPlan.Trading]);
        _scout.GetAsync(Arg.Any<CancellationToken>()).Returns(ScoutPlan(ScoutPlanStatus.Completed));
        _contract.Plans.GetAsync(Arg.Any<CancellationToken>()).Returns(ContractFixture.Plan(ContractMineralPlanStatus.Completed, Start));
        _fleet.Have(
            FleetFixture.Drone("SHIP-1", Start),
            FleetFixture.StartingProbe("SHIP-2", "X1-AB-H59", Start),
            FleetFixture.Drone("SHIP-3", Start));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(5))).Should().BeEmpty();
    }

    [Fact]
    public async Task AMiner_LeftIdleWhileTheContractPlanWaitsForOne_IsAnAnomaly()
    {
        _contract.Plans.GetAsync(Arg.Any<CancellationToken>()).Returns(ContractFixture.Plan(ContractMineralPlanStatus.PendingBudget, Start));
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start));

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(10))).Should().BeEmpty();
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        var violation = violations.Should().ContainSingle().Which;
        violation.Subject.Should().Be("SHIP-1");
        violation.Details.Should().Be(
            "it has had no goal and no assignment for 11 minutes, while the Contract plan has work it could do: contract C-1 waits for a mining ship; it is IN_ORBIT at X1-AB-A1; limit 10 minutes (Health.Ship.MaxIdleMinutes)");
    }

    [Fact]
    public async Task TheScoutShip_LeftIdleWhileStopsRemain_IsAnAnomaly()
    {
        _scout.GetAsync(Arg.Any<CancellationToken>()).Returns(ScoutPlan(ScoutPlanStatus.Active));
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start), FleetFixture.Drone("SHIP-3", Start));

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        violations.Should().ContainSingle().Which.Subject.Should().Be("SHIP-1");
    }

    [Fact]
    public async Task AProbe_LeftIdleWhileADueMarketHasNobodyWatchingIt_IsAnAnomaly()
    {
        // D29: the plan gives every free probe a due market that nothing watches. The starting probe, whose
        // cached type is its role, is a probe (B25).
        _probes.GetAsync(Arg.Any<CancellationToken>()).Returns(ProbePlan(
            new ProbeMarketState { WaypointSymbol = "X1-AB-A1", ProbeSymbol = "SHIP-2" },
            new ProbeMarketState { WaypointSymbol = "X1-AB-B2", DueAt = Start.AddMinutes(-20) }));
        _fleet.Have(FleetFixture.StartingProbe("SHIP-2", "X1-AB-A1", Start));

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        violations.Should().ContainSingle().Which.Should().Match<HealthViolation>(violation =>
            violation.Subject == "SHIP-2" && violation.Details.Contains("1 markets of X1-AB that no probe or ship watches are due", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AProbeInAnotherSystem_IsNoProbeForADueMarketHere()
    {
        // Slice 6.28: the plan flies the probes of each system between that system's markets; one that watches a market abroad
        // is no probe for a due market at home, which the plan gives to home's own probes, a spare or a new one.
        _probes.GetAsync(Arg.Any<CancellationToken>()).Returns(ProbePlan(
            new ProbeMarketState { WaypointSymbol = "X1-AB-A1", ProbeSymbol = "SHIP-2" },
            new ProbeMarketState { WaypointSymbol = "X1-AB-B2", DueAt = Start.AddMinutes(-20) }));
        _fleet.Have(FleetFixture.StartingProbe("SHIP-3", "X1-CD-A1", Start) with { SystemSymbol = "X1-CD" });

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(30))).Should().BeEmpty();
    }

    [Fact]
    public async Task WithEveryMarketWatched_AProbeAtItsMarketIsNotIdleByMistake()
    {
        // D29's long-term goal: a probe at every market, without a goal, watching it.
        _probes.GetAsync(Arg.Any<CancellationToken>()).Returns(ProbePlan(
            new ProbeMarketState { WaypointSymbol = "X1-AB-A1", ProbeSymbol = "SHIP-2" },
            new ProbeMarketState { WaypointSymbol = "X1-AB-B2", WatchedByShip = true }));
        _fleet.Have(FleetFixture.StartingProbe("SHIP-2", "X1-AB-A1", Start));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(30))).Should().BeEmpty();
    }

    [Fact]
    public async Task AMarketSeenWithinTheInterval_IsNoWorkYet()
    {
        _probes.GetAsync(Arg.Any<CancellationToken>()).Returns(ProbePlan(
            new ProbeMarketState { WaypointSymbol = "X1-AB-A1", ProbeSymbol = "SHIP-2" },
            new ProbeMarketState { WaypointSymbol = "X1-AB-B2", DueAt = Start.AddHours(1) }));
        _fleet.Have(FleetFixture.StartingProbe("SHIP-2", "X1-AB-A1", Start));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(30))).Should().BeEmpty();
    }

    [Fact]
    public async Task AMiner_LeftIdleWhileAMiningOpportunityHasNoShip_IsAnAnomaly()
    {
        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>()).Returns(new MiningAutomationPlanState
        {
            PlanId = Guid.NewGuid(),
            Opportunities =
            [
                new MiningAutomationOpportunityState
                {
                    OpportunityKey = "X1-AB-C3|COPPER_ORE",
                    TradeSymbol = "COPPER_ORE",
                    SellWaypointSymbol = "X1-AB-C3",
                    Status = MarketAutomationOpportunityStatus.Pending,
                    CandidateShipSymbols = ["SHIP-3"],
                    FirstObservedAt = Start,
                    LastObservedAt = Start,
                },
            ],
            CreatedAt = Start,
            UpdatedAt = Start,
        });
        _fleet.Have(FleetFixture.Drone("SHIP-3", Start));

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        violations.Should().ContainSingle().Which.Details.Should().Contain("the Mining plan has work it could do: 1 mining opportunities without a ship");
    }

    [Fact]
    public async Task AMinerOutOfReachOfEveryOpening_IsIdleByDesign()
    {
        // Slice 6.4: an opening is work only for the miners the plan lists as able to take it: those that reach its
        // asteroid, or since slice 6.10c, that would drift to its market, with the asteroid within a CRUISE round trip of
        // it (D45). Here the plan lists none.
        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>()).Returns(new MiningAutomationPlanState
        {
            PlanId = Guid.NewGuid(),
            Opportunities =
            [
                new MiningAutomationOpportunityState
                {
                    OpportunityKey = "X1-AB-B7|GOLD_ORE",
                    TradeSymbol = "GOLD_ORE",
                    SellWaypointSymbol = "X1-AB-B7",
                    Status = MarketAutomationOpportunityStatus.Pending,
                    FirstObservedAt = Start,
                    LastObservedAt = Start,
                },
            ],
            CreatedAt = Start,
            UpdatedAt = Start,
        });
        _fleet.Have(FleetFixture.Drone("SHIP-3", Start));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(30))).Should().BeEmpty();
    }

    [Fact]
    public async Task ASiphoner_LeftIdleWhileASiphonOpportunityHasNoShip_IsAnAnomaly()
    {
        // Slice 6.7: as for the miners, an opening is work for the siphoners the plan lists as able to reach its
        // gas giant.
        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.SiphonAutomation, Arg.Any<CancellationToken>()).Returns(new MiningAutomationPlanState
        {
            PlanId = Guid.NewGuid(),
            Opportunities =
            [
                new MiningAutomationOpportunityState
                {
                    OpportunityKey = "X1-AB-G50|LIQUID_HYDROGEN",
                    TradeSymbol = "LIQUID_HYDROGEN",
                    SellWaypointSymbol = "X1-AB-G50",
                    SourceWaypointSymbol = "X1-AB-C38",
                    Status = MarketAutomationOpportunityStatus.Pending,
                    CandidateShipSymbols = ["SHIP-5"],
                    FirstObservedAt = Start,
                    LastObservedAt = Start,
                },
            ],
            CreatedAt = Start,
            UpdatedAt = Start,
        });
        _fleet.Have(FleetFixture.Drone("SHIP-5", Start) with { ShipType = "SHIP_SIPHON_DRONE" }, FleetFixture.Drone("SHIP-3", Start));

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        var violation = violations.Should().ContainSingle().Which;
        violation.Subject.Should().Be("SHIP-5");
        violation.Details.Should().Contain("the Siphon plan has work it could do: 1 siphon opportunities without a ship");

        // With the siphon plan off, the opening waits for nobody.
        _harness.PlansOn.Remove(AutomationPlan.Siphon);
        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(12))).Should().BeEmpty();
    }

    [Fact]
    public async Task AMiner_LeftIdleWhileTheContractStillNeedsUnits_IsAnAnomaly()
    {
        // D23: every free miner joins the contract, not only the plan's first ship.
        _contract.Plans.GetAsync(Arg.Any<CancellationToken>()).Returns(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start));
        _fleet.Have(FleetFixture.Drone("SHIP-4", Start));

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        var violation = violations.Should().ContainSingle().Which;
        violation.Subject.Should().Be("SHIP-4");
        violation.Details.Should().Contain("the Contract plan has work it could do: contract C-1");
    }

    [Fact]
    public async Task ASurveyor_LeftIdleWhileThereIsSomethingToSurvey_IsAnAnomaly()
    {
        // D20: with the survey plan on, a ship that can survey surveys.
        _plans.GetAsync<SurveyPlanState>(PlanTypes.Survey, Arg.Any<CancellationToken>()).Returns(new SurveyPlanState
        {
            PlanId = Guid.NewGuid(),
            Targets =
            [
                new SurveyPlanTarget { TradeSymbol = "COPPER_ORE", WaypointSymbol = "X1-AB-XB5C", BuyerWaypointSymbol = "X1-AB-H51", NeedsSurvey = true, CandidateShipSymbols = ["SHIP-1"] },
                new SurveyPlanTarget { TradeSymbol = "IRON_ORE", WaypointSymbol = "X1-AB-XB5C", BuyerWaypointSymbol = "X1-AB-H51", UsableSurveys = 2, CandidateShipSymbols = ["SHIP-1"] },
            ],
            CreatedAt = Start,
            UpdatedAt = Start,
        });
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start) with { ShipType = "COMMAND", MountSymbols = ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"], CargoCapacity = 40 });

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        violations.Should().ContainSingle().Which.Details.Should().Contain("the Survey plan has work it could do: 1 targets to survey");
    }

    [Fact]
    public async Task AShipThatCanOnlySurvey_LeftIdleWhileATargetItReachesHasItsStock_IsAnAnomaly()
    {
        // D52: a ship that can only survey surveys on once every ore has its stock, so a target it reaches is work for it.
        _plans.GetAsync<SurveyPlanState>(PlanTypes.Survey, Arg.Any<CancellationToken>()).Returns(new SurveyPlanState
        {
            PlanId = Guid.NewGuid(),
            Targets = [new SurveyPlanTarget { TradeSymbol = "COPPER_ORE", WaypointSymbol = "X1-AB-XB5C", BuyerWaypointSymbol = "X1-AB-H51", UsableSurveys = 2, CandidateShipSymbols = ["SHIP-F"] }],
            CreatedAt = Start,
            UpdatedAt = Start,
        });
        _fleet.Have(FleetFixture.Drone("SHIP-F", Start) with { ShipType = "SHIP_SURVEYOR", MountSymbols = ["MOUNT_SURVEYOR_I"], CargoCapacity = 0 });

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        violations.Should().ContainSingle().Which.Details.Should().Contain("the Survey plan has work it could do: 1 targets to survey on");
    }

    [Fact]
    public async Task ASurveyor_OutOfReachOfEveryTargetThatNeedsASurvey_IsIdleByDesign()
    {
        // B55, seen on the cluster on 2026-10-03 at 13:15Z: SPECTER-F, the designated surveyor (an 80-unit tank), waited at
        // XB5C while the targets that needed a survey were at B14, B37, B8 and J72, which only the command ship's 400-unit
        // tank reaches, now that it mines. The plan gives a surveyor only targets it can reach, so that is no work it could
        // give SPECTER-F (D13), as for the mining and siphon openings. (A target it reaches would be work for it, stocked or
        // not: it surveys on, D52.)
        _plans.GetAsync<SurveyPlanState>(PlanTypes.Survey, Arg.Any<CancellationToken>()).Returns(new SurveyPlanState
        {
            PlanId = Guid.NewGuid(),
            Targets =
            [
                new SurveyPlanTarget { TradeSymbol = "GOLD_ORE", WaypointSymbol = "X1-AB-B37", BuyerWaypointSymbol = "X1-AB-B7", NeedsSurvey = true },
                new SurveyPlanTarget { TradeSymbol = "COPPER_ORE", WaypointSymbol = "X1-AB-B14", BuyerWaypointSymbol = "X1-AB-B7", UsableSurveys = 2 },
            ],
            CreatedAt = Start,
            UpdatedAt = Start,
        });
        _fleet.Have(FleetFixture.Drone("SHIP-F", Start) with { ShipType = "SHIP_SURVEYOR", MountSymbols = ["MOUNT_SURVEYOR_I"], CargoCapacity = 0 });

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(30))).Should().BeEmpty();
    }

    [Fact]
    public async Task WithTheRoleBoardOn_AShipThatCanSurvey_ButHasAnotherRole_IsNotLeftIdleBySurveyWork()
    {
        // Slice 6.9 (D38): the ship with the survey role surveys; the command ship trades while a survey ship surveys.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Trade));
        _plans.GetAsync<SurveyPlanState>(PlanTypes.Survey, Arg.Any<CancellationToken>()).Returns(new SurveyPlanState
        {
            PlanId = Guid.NewGuid(),
            Targets = [new SurveyPlanTarget { TradeSymbol = "COPPER_ORE", WaypointSymbol = "X1-AB-XB5C", BuyerWaypointSymbol = "X1-AB-H51", NeedsSurvey = true }],
            CreatedAt = Start,
            UpdatedAt = Start,
        });
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start) with { ShipType = "COMMAND", MountSymbols = ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"], CargoCapacity = 40 });

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(11))).Should().BeEmpty();
    }

    [Fact]
    public async Task TheCommandShip_LeftIdleWhileTheSpareTimePlanListsAPlaceToGather_IsAnAnomaly()
    {
        // Slice 6.8: the plan gives a trip at once to a ship it lists with a place to gather at; idle for long, the
        // plan has stopped.
        _plans.GetAsync<SpareTimePlanState>(PlanTypes.SpareTime, Arg.Any<CancellationToken>()).Returns(new SpareTimePlanState
        {
            PlanId = Guid.NewGuid(),
            Ships =
            [
                new SpareTimeShipState { ShipSymbol = "SHIP-1", Activity = SpareTimeActivity.Gathering, SourceWaypointSymbol = "X1-AB-XB5C" },
                new SpareTimeShipState { ShipSymbol = "SHIP-2", Activity = SpareTimeActivity.Waiting },
            ],
            CreatedAt = Start,
            UpdatedAt = Start,
        });
        var commandShip = FleetFixture.Drone("SHIP-1", Start) with { ShipType = "COMMAND", MountSymbols = ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"], CargoCapacity = 40 };
        _fleet.Have(commandShip, commandShip with { Symbol = "SHIP-2" });

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        var violation = violations.Should().ContainSingle().Which;
        violation.Subject.Should().Be("SHIP-1");
        violation.Details.Should().Contain("the SpareTime plan has work it could do: a place to mine or siphon in its spare time");

        // With the plan off, the command ship waits by design (D13).
        _harness.PlansOn.Remove(AutomationPlan.SpareTime);
        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(12))).Should().BeEmpty();
    }

    [Fact]
    public async Task ABuilder_LeftIdleWhileALoadWaitsForIt_IsAnAnomaly()
    {
        // Slice 6.6: the construction plan gives a free builder a load waits for at once; idle for long, the plan has stopped.
        // A builder no load waits for (no credits, low supply) trades meanwhile, by design.
        _plans.GetAsync<ConstructionPlanState>(PlanTypes.Construction, Arg.Any<CancellationToken>()).Returns(new ConstructionPlanState
        {
            Sites = [],
            BuilderShipSymbols = ["SHIP-6", "SHIP-7"],
            ReadyShipSymbols = ["SHIP-6"],
            UpdatedAt = Start,
        });
        var hauler = FleetFixture.Drone("SHIP-6", Start) with { ShipType = "SHIP_LIGHT_HAULER", MountSymbols = [], CargoCapacity = 80 };
        _fleet.Have(hauler, hauler with { Symbol = "SHIP-7" });

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        var violation = violations.Should().ContainSingle().Which;
        violation.Subject.Should().Be("SHIP-6");
        violation.Details.Should().Contain("the Construction plan has work it could do: a load of materials for the jump gate");

        _harness.PlansOn.Remove(AutomationPlan.Construction);
        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(12))).Should().BeEmpty();
    }

    [Fact]
    public async Task ASurveyorWaiting_WhileEveryOreHasItsStockOfSurveys_IsNotAnAnomaly()
    {
        // D27: with a stock of usable surveys for every ore, there is nothing to survey; the surveyor waits. The command ship can
        // do more than survey, so it doesn't survey on (D52 is for a ship that can only survey).
        _plans.GetAsync<SurveyPlanState>(PlanTypes.Survey, Arg.Any<CancellationToken>()).Returns(new SurveyPlanState
        {
            PlanId = Guid.NewGuid(),
            Targets = [new SurveyPlanTarget { TradeSymbol = "COPPER_ORE", WaypointSymbol = "X1-AB-XB5C", BuyerWaypointSymbol = "X1-AB-H51", UsableSurveys = 2, CandidateShipSymbols = ["SHIP-1"] }],
            CreatedAt = Start,
            UpdatedAt = Start,
        });
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start) with { ShipType = "COMMAND", MountSymbols = ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"], CargoCapacity = 40 });

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(11));

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task ATrader_LeftIdleWhileALucrativeRouteItCouldTakeHasNoShip_IsAnAnomaly()
    {
        OpenTradeRoute(candidates: ["SHIP-5"]);
        _fleet.Have(new ShipModel("SHIP-5", "X1-AB", "X1-AB-B2", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 40, LastSyncedAt: Start, ShipType: "SHIP_LIGHT_HAULER"));

        await _harness.EvaluateAsync(_rule, Start);

        var violation = (await _harness.EvaluateAsync(_rule, Start.AddMinutes(11))).Should().ContainSingle().Subject;
        violation.Subject.Should().Be("SHIP-5");
        violation.Details.Should().Contain("the Trading plan has work it could do: 1 lucrative trade routes without a ship");
    }

    [Fact]
    public async Task ATrader_WhoseTankCantReachTheOpenRoutes_IsIdleByDesign()
    {
        // Slice 6.5: the open route is the command ship's to take; the drone's 80-unit tank can't
        // fly it, so the plan doesn't list the drone among its candidates.
        OpenTradeRoute(candidates: ["SHIP-1"]);
        _fleet.Have(new ShipModel("SHIP-3", "X1-AB", "X1-AB-B2", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 15, LastSyncedAt: Start, ShipType: "EXCAVATOR"));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(30))).Should().BeEmpty();
    }

    private void OpenTradeRoute(IReadOnlyList<string> candidates)
        => _plans.GetAsync<TradingAutomationPlanState>(PlanTypes.TradingAutomation, Arg.Any<CancellationToken>()).Returns(new TradingAutomationPlanState
        {
            PlanId = Guid.NewGuid(),
            Opportunities =
            [
                new TradingAutomationOpportunityState
                {
                    OpportunityKey = "X1-AB-B2|X1-AB-C3|FOOD",
                    TradeSymbol = "FOOD",
                    BuyWaypointSymbol = "X1-AB-B2",
                    SellWaypointSymbol = "X1-AB-C3",
                    Status = MarketAutomationOpportunityStatus.Pending,
                    CandidateShipSymbols = candidates,
                    FirstObservedAt = Start,
                    LastObservedAt = Start,
                },
            ],
            CreatedAt = Start,
            UpdatedAt = Start,
        });

    [Fact]
    public async Task AShipWithWork_OrInTransit_IsNotIdle()
    {
        _contract.Plans.GetAsync(Arg.Any<CancellationToken>()).Returns(ContractFixture.Plan(ContractMineralPlanStatus.PendingBudget, Start));
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start), FleetFixture.Drone("SHIP-3", Start, arrivesAt: Start.AddHours(1)));
        _fleet.Give("SHIP-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2" });

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(30))).Should().BeEmpty();
    }

    [Fact]
    public async Task WorkThatTurnsUp_StartsTheClock()
    {
        _contract.Plans.GetAsync(Arg.Any<CancellationToken>()).Returns(ContractFixture.Plan(ContractMineralPlanStatus.Completed, Start));
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start.AddHours(-3)));
        await _harness.EvaluateAsync(_rule, Start);

        _contract.Plans.GetAsync(Arg.Any<CancellationToken>()).Returns(ContractFixture.Plan(ContractMineralPlanStatus.PendingBudget, Start.AddMinutes(20)));
        await _harness.EvaluateAsync(_rule, Start.AddMinutes(20));

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(25))).Should().BeEmpty();
        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(31))).Should().ContainSingle();
    }

    [Fact]
    public async Task TheLimit_IsASetting()
    {
        _settings.GetAsync<int>(ShipLeftIdleRule.Setting, Arg.Any<CancellationToken>()).Returns(2);
        _contract.Plans.GetAsync(Arg.Any<CancellationToken>()).Returns(ContractFixture.Plan(ContractMineralPlanStatus.PendingBudget, Start));
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(3))).Should().ContainSingle();
    }

    private static ScoutAllMarketplacesPlanState ScoutPlan(ScoutPlanStatus status) => new()
    {
        PlanId = Guid.NewGuid(),
        ShipSymbol = "SHIP-1",
        StartWaypointSymbol = "X1-AB-A1",
        RouteWaypointSymbols = ["X1-AB-A1", "X1-AB-B2"],
        CurrentRouteIndex = 1,
        Status = status,
        CreatedAt = Start,
        UpdatedAt = Start,
    };

    private static ProbeDeploymentPlanState ProbePlan(params ProbeMarketState[] markets) => new()
    {
        PlanId = Guid.NewGuid(),
        SystemSymbol = "X1-AB",
        Probes = 1,
        Systems = [new ProbeSystemState { SystemSymbol = "X1-AB", Reached = true, InTradeReach = true, Probes = 1, Markets = markets }],
        CreatedAt = Start,
        UpdatedAt = Start,
    };
}

/// <summary>Phase 3.2: the circuit breaker hasn't tripped.</summary>
public sealed class CircuitBreakerTrippedRuleTests
{
    private static readonly DateTimeOffset Start = RuleHarness.Start;

    private readonly FleetFixture _fleet = new();
    private readonly IGoalStepCircuitBreaker _breaker = Substitute.For<IGoalStepCircuitBreaker>();
    private readonly RuleHarness _harness = new();
    private readonly CircuitBreakerTrippedRule _rule;

    public CircuitBreakerTrippedRuleTests()
    {
        _breaker.LastTrips.Returns(new Dictionary<string, DateTimeOffset>());
        _rule = new CircuitBreakerTrippedRule(_fleet.Fleet, _breaker);
    }

    [Fact]
    public async Task AShipWhoseGoalIsBlocked_IsAnAnomaly()
    {
        _fleet.Have(FleetFixture.Drone("SHIP-1", Start));
        _fleet.Give("SHIP-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2", Status = GoalStatus.Blocked, StatusReason = "runaway" });

        var violations = await _harness.EvaluateAsync(_rule, Start);

        violations.Should().ContainSingle().Which.Should().Be(new HealthViolation(
            "SHIP-1",
            "its ScoutWaypoint goal is blocked (runaway): it took more goal steps in a minute than Automation.CircuitBreaker.MaxGoalStepsPerMinute allows, and it stays blocked until a plan replaces it"));
    }

    [Fact]
    public async Task ATripInTheLastHour_Counts_AfterAPlanReplacedTheGoal()
    {
        _fleet.Have(FleetFixture.Drone("SHIP-3", Start));
        _fleet.Give("SHIP-3", new MineAndSellGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-AB-A1", SellWaypointSymbol = "X1-AB-C3" });
        _breaker.LastTrips.Returns(new Dictionary<string, DateTimeOffset> { ["SHIP-3"] = Start.AddMinutes(-10) });

        var violations = await _harness.EvaluateAsync(_rule, Start);

        violations.Should().ContainSingle().Which.Details.Should().StartWith("the circuit breaker blocked its goal 10 minutes ago (2026-10-01 11:50:00Z)");
    }

    [Fact]
    public async Task AnOlderTrip_IsOver()
    {
        _fleet.Have(FleetFixture.Drone("SHIP-3", Start));
        _breaker.LastTrips.Returns(new Dictionary<string, DateTimeOffset> { ["SHIP-3"] = Start.AddMinutes(-61) });

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
    }
}
