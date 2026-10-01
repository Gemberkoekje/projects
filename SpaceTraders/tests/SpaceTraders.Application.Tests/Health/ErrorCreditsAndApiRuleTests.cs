using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Health;

/// <summary>Phase 3.2: the same error repeats at most N times in 10 minutes.</summary>
public sealed class RepeatingErrorRuleTests
{
    private const string Statement = "GameLoopService: the goal step for ship {ShipSymbol} failed; the rest of the tick carries on.";

    private static readonly DateTimeOffset Start = RuleHarness.Start;

    private readonly ErrorLog _errors = new();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly RuleHarness _harness = new();
    private readonly RepeatingErrorRule _rule;

    public RepeatingErrorRuleTests()
    {
        _rule = new RepeatingErrorRule(_errors, _settings);
    }

    [Fact]
    public async Task AStatementLoggingMoreThanFiveTimesInTenMinutes_IsARepeatingError()
    {
        await _harness.EvaluateAsync(_rule, Start);
        Log(times: 6, from: Start.AddMinutes(1));

        var violations = await _harness.EvaluateAsync(_rule, Start.AddMinutes(7));

        var violation = violations.Should().ContainSingle().Which;
        violation.Subject.Should().Be(Statement);
        violation.Details.Should().Be(
            "logged 6 warnings or errors in the last 10 minutes, the last at 2026-10-01 12:01:50Z: GameLoopService: the goal step for ship \"SHIP-1\" failed; the rest of the tick carries on.; limit 5 (Health.Errors.MaxRepeatsIn10Minutes)");
    }

    [Fact]
    public async Task FiveTimes_IsWithinTheLimit()
    {
        await _harness.EvaluateAsync(_rule, Start);
        Log(times: 5, from: Start.AddMinutes(1));

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(7))).Should().BeEmpty();
    }

    [Fact]
    public async Task WhatWasLoggedMoreThanTenMinutesAgo_DoesntCount()
    {
        await _harness.EvaluateAsync(_rule, Start);
        Log(times: 6, from: Start.AddMinutes(1));

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(13))).Should().BeEmpty();
    }

    [Fact]
    public async Task WhatStartupLogged_DoesntCount()
    {
        // Startup has its own failure handling: a failed startup stops the host.
        Log(times: 20, from: Start.AddMinutes(-5));

        (await _harness.EvaluateAsync(_rule, Start)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLimit_IsASetting()
    {
        _settings.GetAsync<int>(RepeatingErrorRule.Setting, Arg.Any<CancellationToken>()).Returns(1);
        await _harness.EvaluateAsync(_rule, Start);
        Log(times: 2, from: Start.AddMinutes(1));

        (await _harness.EvaluateAsync(_rule, Start.AddMinutes(3))).Should().ContainSingle();
    }

    [Fact]
    public void TheLog_ForgetsWhatIsOlderThanTenMinutes()
    {
        Log(times: 3, from: Start);
        _errors.Record("Other: something else.", "something else.", Start.AddMinutes(11));

        _errors.Since(DateTimeOffset.MinValue).Select(statement => statement.Statement).Should().Equal("Other: something else.");
    }

    private void Log(int times, DateTimeOffset from)
    {
        for (var index = 0; index < times; index++)
        {
            _errors.Record(Statement, "GameLoopService: the goal step for ship \"SHIP-1\" failed; the rest of the tick carries on.", from.AddSeconds(10 * index));
        }
    }
}

/// <summary>Phase 3.2 with D13: credits change at least once in 24 hours while the fleet has work.</summary>
public sealed class CreditsUnchangedRuleTests
{
    private static readonly DateTimeOffset Start = RuleHarness.Start;

    private readonly FleetFixture _fleet = new();
    private readonly IAgentCreditsSampleRepository _samples = Substitute.For<IAgentCreditsSampleRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly RuleHarness _harness = new();
    private readonly CreditsUnchangedRule _rule;

    public CreditsUnchangedRuleTests()
    {
        _samples.GetRangeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<CreditsSampleDto>());
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, "X1-AB-A1", 137_184, "COSMIC", 3));
        _fleet.Have(FleetFixture.Drone("SHIP-3", Start));
        _rule = new CreditsUnchangedRule(_fleet.Fleet, _samples, _agents, _settings);
    }

    [Fact]
    public async Task AFleetAtWork_WhoseCreditsDontChangeFor24Hours_IsAnAnomaly()
    {
        _fleet.Assign(FleetFixture.ContractAssignment("SHIP-3", "C-1"));

        await _harness.EvaluateAsync(_rule, Start);
        (await _harness.EvaluateAsync(_rule, Start.AddHours(24))).Should().BeEmpty();
        var violations = await _harness.EvaluateAsync(_rule, Start.AddHours(24).AddMinutes(1));

        violations.Should().ContainSingle().Which.Should().Be(new HealthViolation(
            "SPECTER",
            "the credits (137,184) haven't changed for 24 hours, while the fleet had work all that time (now SHIP-3); limit 24 hours (Health.Credits.MaxHoursUnchanged)"));
        await _samples.Received().GetRangeAsync(Start.AddMinutes(1), Start.AddHours(24).AddMinutes(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACreditChange_InTheLast24Hours_IsEnough()
    {
        _fleet.Assign(FleetFixture.ContractAssignment("SHIP-3", "C-1"));
        _samples.GetRangeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new CreditsSampleDto(Start.AddHours(20), 140_000)]);

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(30))).Should().BeEmpty();
    }

    [Fact]
    public async Task AnIdleFleet_IsntExpectedToChangeTheCredits()
    {
        // D1, D9: after its one contract the first run's fleet is idle by design.
        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(30))).Should().BeEmpty();
    }

    [Fact]
    public async Task TheClock_StartsWhenTheFleetGetsWork()
    {
        await _harness.EvaluateAsync(_rule, Start);
        _fleet.Give("SHIP-3", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2" });
        await _harness.EvaluateAsync(_rule, Start.AddHours(10));

        (await _harness.EvaluateAsync(_rule, Start.AddHours(30))).Should().BeEmpty();
        (await _harness.EvaluateAsync(_rule, Start.AddHours(34).AddMinutes(1))).Should().ContainSingle();
    }

    [Fact]
    public async Task WorkOfAPlanThatIsOff_DoesntCount()
    {
        _fleet.Assign(FleetFixture.ContractAssignment("SHIP-3", "C-1"));
        _harness.PlansOn.Remove(AutomationPlan.Contract);

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(30))).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLimit_IsASetting()
    {
        _settings.GetAsync<int>(CreditsUnchangedRule.Setting, Arg.Any<CancellationToken>()).Returns(2);
        _fleet.Assign(FleetFixture.ContractAssignment("SHIP-3", "C-1"));

        await _harness.EvaluateAsync(_rule, Start);

        (await _harness.EvaluateAsync(_rule, Start.AddHours(2).AddMinutes(1))).Should().ContainSingle();
    }
}

/// <summary>Phase 3.2: no 401 or reset errors, and at most N 429s an hour.</summary>
public sealed class ApiRuleTests
{
    private static readonly DateTimeOffset Start = RuleHarness.Start;

    private readonly ApiResponseLog _responses = new();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly RuleHarness _harness = new();

    [Fact]
    public async Task A401_AfterStartup_IsAnAnomaly()
    {
        var rule = new ApiUnauthorizedRule(_responses);
        await _harness.EvaluateAsync(rule, Start);
        _responses.RecordUnauthorized("my/ships/{shipSymbol}/navigate", Start.AddMinutes(2));

        var violations = await _harness.EvaluateAsync(rule, Start.AddMinutes(3));

        violations.Should().ContainSingle().Which.Should().Be(new HealthViolation(
            "api",
            "the SpaceTraders API answered 401 Unauthorized once in the last hour, the last time for my/ships/{shipSymbol}/navigate at 2026-10-01 12:02:00Z"));
    }

    [Fact]
    public async Task A401_DuringStartup_IsAgentBootstrapTryingOldTokens()
    {
        var rule = new ApiUnauthorizedRule(_responses);
        _responses.RecordUnauthorized("my/agent", Start.AddSeconds(-30));

        (await _harness.EvaluateAsync(rule, Start)).Should().BeEmpty();
    }

    [Fact]
    public async Task A401_MoreThanAnHourAgo_IsOver()
    {
        var rule = new ApiUnauthorizedRule(_responses);
        await _harness.EvaluateAsync(rule, Start);
        _responses.RecordUnauthorized("my/agent", Start.AddMinutes(1));

        (await _harness.EvaluateAsync(rule, Start.AddMinutes(62))).Should().BeEmpty();
    }

    [Fact]
    public async Task MoreThanTen429sInAnHour_IsAnAnomaly()
    {
        var rule = new ApiThrottledRule(_responses, _settings);
        for (var index = 0; index < 11; index++)
        {
            _responses.RecordThrottled("my/agent", index < 8 ? "rate_limiter" : "infrastructure", Start.AddMinutes(index));
        }

        var violations = await _harness.EvaluateAsync(rule, Start.AddMinutes(20));

        violations.Should().ContainSingle().Which.Should().Be(new HealthViolation(
            "api",
            "the SpaceTraders API answered 429 Too Many Requests 11 times in the last hour, 8 from its rate limiter and 3 from its infrastructure; limit 10 (Health.Api.Max429sPerHour)"));
    }

    [Fact]
    public async Task Ten429sInAnHour_IsWithinTheLimit()
    {
        var rule = new ApiThrottledRule(_responses, _settings);
        for (var index = 0; index < 11; index++)
        {
            _responses.RecordThrottled("my/agent", "infrastructure", Start.AddMinutes(index * 6));
        }

        // The first is more than an hour old by now.
        (await _harness.EvaluateAsync(rule, Start.AddMinutes(61))).Should().BeEmpty();
    }

    [Fact]
    public async Task The429Limit_IsASetting()
    {
        _settings.GetAsync<int>(ApiThrottledRule.Setting, Arg.Any<CancellationToken>()).Returns(1);
        var rule = new ApiThrottledRule(_responses, _settings);
        _responses.RecordThrottled("my/agent", "rate_limiter", Start);
        _responses.RecordThrottled("my/agent", "rate_limiter", Start);

        (await _harness.EvaluateAsync(rule, Start.AddMinutes(1))).Should().ContainSingle();
    }
}
