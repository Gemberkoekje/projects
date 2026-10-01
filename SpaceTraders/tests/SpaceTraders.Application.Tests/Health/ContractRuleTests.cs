using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Tests.Health;

/// <summary>Phase 3.2: an accepted contract makes progress within N hours.</summary>
public sealed class ContractStalledRuleTests
{
    private static readonly DateTimeOffset Start = RuleHarness.Start;
    private static readonly DateTimeOffset FarDeadline = Start.AddDays(6);

    private readonly ContractFixture _contract = new();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly RuleHarness _harness = new();
    private readonly ContractStalledRule _rule;

    public ContractStalledRuleTests()
    {
        _rule = new ContractStalledRule(_contract.Plans, _contract.Contracts, _settings);
    }

    [Fact]
    public async Task AContract_WithoutADeliveryForMoreThanFourHours_IsStalled()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start.AddHours(-1)), ContractFixture.Contract(11, FarDeadline));

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
        (await _harness.EvaluateAsync(_rule, Start.AddHours(4))).Should().BeEmpty();
        var violations = await _harness.EvaluateAsync(_rule, Start.AddHours(4).AddMinutes(1));

        var violation = violations.Should().ContainSingle().Which;
        violation.Subject.Should().Be(ContractFixture.ContractId);
        violation.Details.Should().Be(
            "no units delivered for 4 hours, since 2026-10-01 12:00:00Z; 11/42 IRON_ORE delivered, contract plan Active; limit 4 hours (Health.Contract.MaxHoursWithoutProgress)");
    }

    [Fact]
    public async Task ADelivery_StartsTheClockAgain()
    {
        // The plan writes its state only when the units delivered change.
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start.AddHours(3)), ContractFixture.Contract(26, FarDeadline));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(5))).Should().BeEmpty();
    }

    [Fact]
    public async Task TheClock_StartsNoEarlierThanTheMonitorSawThePlanWorking()
    {
        // After a restart, or once automation is back on, a contract gets its full time.
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start.AddHours(-10)), ContractFixture.Contract(11, FarDeadline));

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
        (await _harness.EvaluateAsync(_rule, Start.AddHours(3))).Should().BeEmpty();
    }

    [Fact]
    public async Task AContractWaitingForAShip_IsStalledToo()
    {
        _contract.Is(
            ContractFixture.Plan(ContractMineralPlanStatus.PendingBudget, Start, stopReason: "No idle mining ship available and unable to purchase SHIP_MINING_DRONE."),
            ContractFixture.Contract(0, FarDeadline));

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddHours(5));

        violations.Should().ContainSingle().Which.Details.Should().Contain(
            "contract plan PendingBudget: No idle mining ship available and unable to purchase SHIP_MINING_DRONE;");
    }

    [Fact]
    public async Task AMineralContractWithoutAnAsteroid_IsStalled()
    {
        _contract.Is(
            ContractFixture.Plan(ContractMineralPlanStatus.DeferredUnsupported, Start, stopReason: "No asteroid source waypoint found for mineral extraction."),
            ContractFixture.Contract(0, FarDeadline));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(5))).Should().ContainSingle();
    }

    [Fact]
    public async Task AParkedNonMineralContract_IsLeftOut()
    {
        // D2: a contract whose deliverable isn't a mineral is parked on purpose.
        _contract.Is(
            ContractFixture.Plan(ContractMineralPlanStatus.DeferredUnsupported, Start, tradeSymbol: "FOOD", stopReason: "Unsupported non-mineral deliverable: FOOD."),
            ContractFixture.Contract(0, FarDeadline, tradeSymbol: "FOOD"));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(48))).Should().BeEmpty();
    }

    [Fact]
    public async Task WhileTheContractPlanIsOff_NothingStalls()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(11, FarDeadline));
        _harness.PlansOn.Remove(AutomationPlan.Contract);

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(48))).Should().BeEmpty();
    }

    [Fact]
    public async Task AFulfilledContract_IsNotStalled()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(42, FarDeadline, isFulfilled: true));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(48))).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLimit_IsASetting()
    {
        _settings.GetAsync<int>(ContractStalledRule.Setting, Arg.Any<CancellationToken>()).Returns(1);
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(11, FarDeadline));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(61))).Should().ContainSingle();
    }
}

/// <summary>Phase 3.2: a fulfilled contract has no active plan or assignment (B9).</summary>
public sealed class ContractLeftOpenRuleTests
{
    private static readonly DateTimeOffset Start = RuleHarness.Start;

    private readonly ContractFixture _contract = new();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly RuleHarness _harness = new();
    private readonly ContractLeftOpenRule _rule;

    public ContractLeftOpenRuleTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _rule = new ContractLeftOpenRule(_contract.Plans, _contract.Contracts, _assignments);
    }

    [Fact]
    public async Task AFulfilledContract_WhosePlanStaysActive_IsLeftOpen()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(42, Start.AddDays(6), isFulfilled: true));

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(5))).Should().BeEmpty();
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(6));

        var violation = violations.Should().ContainSingle().Which;
        violation.Subject.Should().Be(ContractFixture.ContractId);
        violation.Details.Should().Be("the contract is fulfilled, but the contract plan is still Active, for 6 minutes; the contract plan closes both on its next run");
    }

    [Fact]
    public async Task AFulfilledContract_WhoseShipKeepsItsAssignment_IsLeftOpen()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Completed, Start), ContractFixture.Contract(42, Start.AddDays(6), isFulfilled: true));
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([FleetFixture.ContractAssignment("SHIP-3", ContractFixture.ContractId)]);

        await _harness.EvaluateAsync(_rule, Start);
        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(6));

        violations.Should().ContainSingle().Which.Details.Should().StartWith("the contract is fulfilled, but ship SHIP-3 still has its contract assignment");
    }

    [Fact]
    public async Task AFulfilledContract_ThatThePlanClosed_IsFine()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Completed, Start), ContractFixture.Contract(42, Start.AddDays(6), isFulfilled: true));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(1))).Should().BeEmpty();
    }

    [Fact]
    public async Task AContractThatIsntFulfilled_KeepsItsPlanAndAssignment()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(30, Start.AddDays(6)));
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([FleetFixture.ContractAssignment("SHIP-3", ContractFixture.ContractId)]);

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(1))).Should().BeEmpty();
    }

    [Fact]
    public async Task WhileTheContractPlanIsOff_NothingClosesThem_OnPurpose()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(42, Start.AddDays(6), isFulfilled: true));
        _harness.PlansOn.Remove(AutomationPlan.Contract);

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(1))).Should().BeEmpty();
    }
}

/// <summary>Phase 3.2: a deadline within N hours comes with at least X% delivered.</summary>
public sealed class ContractDeadlineAtRiskRuleTests
{
    private static readonly DateTimeOffset Start = RuleHarness.Start;

    private readonly ContractFixture _contract = new();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly RuleHarness _harness = new();
    private readonly ContractDeadlineAtRiskRule _rule;

    public ContractDeadlineAtRiskRuleTests()
    {
        _rule = new ContractDeadlineAtRiskRule(_contract.Plans, _contract.Contracts, _settings);
    }

    [Fact]
    public async Task ADeadlineWithin24Hours_WithLessThanHalfDelivered_IsAtRisk()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(10, Start.AddHours(20)));

        var violations = await _harness.EvaluateAsync(_rule, Start);

        var violation = violations.Should().ContainSingle().Which;
        violation.Subject.Should().Be(ContractFixture.ContractId);
        violation.Details.Should().Be(
            "its deadline is in 20 hours (2026-10-02 08:00:00Z) with 10/42 IRON_ORE delivered (23%); from 24 hours before the deadline at least 50% should be (Health.Contract.DeadlineHours, Health.Contract.MinDeliveredPercent)");
    }

    [Fact]
    public async Task HalfDelivered_IsEnough()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(21, Start.AddHours(20)));

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
    }

    [Fact]
    public async Task ADeadlineFurtherAway_IsNoRiskYet()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(0, Start.AddHours(25)));

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
    }

    [Fact]
    public async Task AMissedDeadline_IsAnAnomaly_WhateverWasDelivered()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(40, Start.AddHours(-2)));

        var violations = await _harness.EvaluateAsync(_rule, Start);

        violations.Should().ContainSingle().Which.Details.Should().Be(
            "it missed its deadline 2 hours ago (2026-10-01 10:00:00Z) with 40/42 IRON_ORE delivered (95%)");
    }

    [Fact]
    public async Task AParkedNonMineralContract_IsLeftOut()
    {
        // D2: nobody works on it, by decision.
        _contract.Is(
            ContractFixture.Plan(ContractMineralPlanStatus.DeferredUnsupported, Start, tradeSymbol: "FOOD"),
            ContractFixture.Contract(0, Start.AddHours(2), tradeSymbol: "FOOD"));

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
    }

    [Fact]
    public async Task WhileTheContractPlanIsOff_NothingIsExpected()
    {
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(0, Start.AddHours(2)));
        _harness.PlansOn.Remove(AutomationPlan.Contract);

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheHoursAndThePercentage_AreSettings()
    {
        _settings.GetAsync<int>(ContractDeadlineAtRiskRule.HoursSetting, Arg.Any<CancellationToken>()).Returns(48);
        _settings.GetAsync<int>(ContractDeadlineAtRiskRule.PercentSetting, Arg.Any<CancellationToken>()).Returns(90);
        _contract.Is(ContractFixture.Plan(ContractMineralPlanStatus.Active, Start), ContractFixture.Contract(30, Start.AddHours(40)));

        (await _harness.EvaluateAsync(_rule, Start)).Should().ContainSingle();
    }
}
