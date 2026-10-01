using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Tests.Health;

/// <summary>
/// Phase 3.1: every minute the monitor evaluates the health rules. A subject that breaks a rule is an
/// anomaly: <c>spacetraders_anomaly_active{rule,subject}</c> and the journal's <c>AnomalyRaised</c> and
/// <c>AnomalyCleared</c>.
/// </summary>
public sealed class HealthMonitorServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 01, 12, 00, 00, TimeSpan.Zero);

    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly IApiAvailabilityState _api = Substitute.For<IApiAvailabilityState>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly LogRecorder _log = new();
    private readonly FakeRule _ships = new("ShipStuck");
    private readonly FakeRule _contracts = new("ContractStalled");
    private readonly ServiceProvider _provider;
    private readonly HealthMonitorService _monitor;

    public HealthMonitorServiceTests()
    {
        _settings.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _api.PausedUntil.Returns(DateTimeOffset.MinValue);
        _provider = new ServiceCollection()
            .AddScoped(_ => _settings)
            .AddScoped<IHealthRule>(_ => _ships)
            .AddScoped<IHealthRule>(_ => _contracts)
            .BuildServiceProvider();
        _monitor = new HealthMonitorService(_provider.GetRequiredService<IServiceScopeFactory>(), _metrics, _api, _log.For<HealthMonitorService>());
    }

    [Fact]
    public async Task AViolation_IsRaisedOnce_AsAMetricAndAJournalLine()
    {
        _ships.Violations = [new HealthViolation("SHIP-1", "nothing changed for 31 minutes")];

        await _monitor.EvaluateAsync(Start, CancellationToken.None);
        await _monitor.EvaluateAsync(Start.AddMinutes(1), CancellationToken.None);

        _metrics.Received(1).Anomaly("ShipStuck", "SHIP-1", true);
        var raised = _log.Journal.Should().ContainSingle().Which;
        raised.Level.Should().Be(LogLevel.Warning);
        raised.EventKind.Should().Be(JournalEvents.AnomalyRaised);
        raised.Properties["Rule"].Should().Be("ShipStuck");
        raised.Properties["Subject"].Should().Be("SHIP-1");
        raised.Properties["Details"].Should().Be("nothing changed for 31 minutes");
        raised.Message.Should().Be("AnomalyRaised: ShipStuck on SHIP-1: nothing changed for 31 minutes.");
    }

    [Fact]
    public async Task AnAnomaly_ClearsOnceTheRuleHoldsAgain()
    {
        _ships.Violations = [new HealthViolation("SHIP-1", "stuck")];
        await _monitor.EvaluateAsync(Start, CancellationToken.None);

        _ships.Violations = [];
        await _monitor.EvaluateAsync(Start.AddMinutes(7), CancellationToken.None);
        await _monitor.EvaluateAsync(Start.AddMinutes(8), CancellationToken.None);

        _metrics.Received(1).Anomaly("ShipStuck", "SHIP-1", false);
        var cleared = _log.Journal.Should().HaveCount(2).And.Subject.Last();
        cleared.Level.Should().Be(LogLevel.Information);
        cleared.EventKind.Should().Be(JournalEvents.AnomalyCleared);
        cleared.Properties["ActiveMinutes"].Should().Be(7);
        cleared.Message.Should().Be("AnomalyCleared: ShipStuck on SHIP-1 ended after 7 minutes.");
    }

    [Fact]
    public async Task EachSubject_IsAnAnomalyOfItsOwn()
    {
        _ships.Violations = [new HealthViolation("SHIP-1", "stuck"), new HealthViolation("SHIP-2", "stuck")];
        await _monitor.EvaluateAsync(Start, CancellationToken.None);

        _ships.Violations = [new HealthViolation("SHIP-2", "still stuck")];
        await _monitor.EvaluateAsync(Start.AddMinutes(1), CancellationToken.None);

        _metrics.Received(1).Anomaly("ShipStuck", "SHIP-1", true);
        _metrics.Received(1).Anomaly("ShipStuck", "SHIP-2", true);
        _metrics.Received(1).Anomaly("ShipStuck", "SHIP-1", false);
        _metrics.DidNotReceive().Anomaly("ShipStuck", "SHIP-2", false);
    }

    [Fact]
    public async Task ARuleThatThrows_KeepsItsAnomalies_AndTheOthersStillRun()
    {
        _ships.Violations = [new HealthViolation("SHIP-1", "stuck")];
        await _monitor.EvaluateAsync(Start, CancellationToken.None);

        _ships.Throws = true;
        _contracts.Violations = [new HealthViolation("C-1", "no delivery for 5 hours")];
        await _monitor.EvaluateAsync(Start.AddMinutes(1), CancellationToken.None);

        _metrics.DidNotReceive().Anomaly("ShipStuck", "SHIP-1", false);
        _metrics.Received(1).Anomaly("ContractStalled", "C-1", true);
        _log.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error)
            .Which.Message.Should().Be("Health rule ShipStuck failed; its anomalies stay as they are until it runs again.");
    }

    [Fact]
    public async Task WorkingSince_StartsWhenAutomationAndThePlanAreOn_AndNotBeforeTheMonitor()
    {
        var seen = new List<DateTimeOffset>();
        _ships.OnEvaluate = context => seen.Add(context.WorkingSince(AutomationPlan.Contract));

        await _monitor.EvaluateAsync(Start, CancellationToken.None);
        await _monitor.EvaluateAsync(Start.AddMinutes(1), CancellationToken.None);

        _settings.GetAsync<bool>(AutomationSwitches.EnabledSetting, Arg.Any<CancellationToken>()).Returns(false);
        await _monitor.EvaluateAsync(Start.AddMinutes(2), CancellationToken.None);

        _settings.GetAsync<bool>(AutomationSwitches.EnabledSetting, Arg.Any<CancellationToken>()).Returns(true);
        await _monitor.EvaluateAsync(Start.AddMinutes(3), CancellationToken.None);

        seen.Should().Equal(Start, Start, DateTimeOffset.MaxValue, Start.AddMinutes(3));
    }

    [Fact]
    public async Task WorkingSince_StartsAgainAfterAnApiPause()
    {
        var seen = new List<DateTimeOffset>();
        _ships.OnEvaluate = context => seen.Add(context.WorkingSince(AutomationPlan.Scout));

        await _monitor.EvaluateAsync(Start, CancellationToken.None);
        _api.PausedUntil.Returns(Start.AddMinutes(3));
        await _monitor.EvaluateAsync(Start.AddMinutes(1), CancellationToken.None);
        await _monitor.EvaluateAsync(Start.AddMinutes(4), CancellationToken.None);

        seen.Should().Equal(Start, DateTimeOffset.MaxValue, Start.AddMinutes(4));
    }

    [Fact]
    public async Task APlanThatIsOff_IsNotWorking()
    {
        var seen = new List<(bool IsOn, DateTimeOffset Since)>();
        _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Mining), Arg.Any<CancellationToken>()).Returns(false);
        _ships.OnEvaluate = context => seen.Add((context.IsOn(AutomationPlan.Mining), context.WorkingSince(AutomationPlan.Mining)));

        await _monitor.EvaluateAsync(Start, CancellationToken.None);

        seen.Should().Equal((false, DateTimeOffset.MaxValue));
    }

    [Fact]
    public async Task HeldSince_RemembersAConditionUntilItStopsHolding_OrNoRuleAsks()
    {
        var seen = new List<DateTimeOffset>();
        var holds = true;
        var ask = true;
        _ships.OnEvaluate = context =>
        {
            if (ask)
            {
                seen.Add(context.HeldSince("idle:SHIP-1", holds));
            }
        };

        await _monitor.EvaluateAsync(Start, CancellationToken.None);
        await _monitor.EvaluateAsync(Start.AddMinutes(1), CancellationToken.None);
        holds = false;
        await _monitor.EvaluateAsync(Start.AddMinutes(2), CancellationToken.None);
        holds = true;
        await _monitor.EvaluateAsync(Start.AddMinutes(3), CancellationToken.None);
        ask = false;
        await _monitor.EvaluateAsync(Start.AddMinutes(4), CancellationToken.None);
        ask = true;
        await _monitor.EvaluateAsync(Start.AddMinutes(5), CancellationToken.None);

        seen.Should().Equal(Start, Start, DateTimeOffset.MaxValue, Start.AddMinutes(3), Start.AddMinutes(5));
    }

    [Fact]
    public async Task TheContext_KnowsWhenTheMonitorStarted()
    {
        var seen = new List<DateTimeOffset>();
        _ships.OnEvaluate = context => seen.Add(context.MonitorStartedAt);

        await _monitor.EvaluateAsync(Start, CancellationToken.None);
        await _monitor.EvaluateAsync(Start.AddMinutes(1), CancellationToken.None);

        seen.Should().Equal(Start, Start);
    }

    public void Dispose()
    {
        _monitor.Dispose();
        _provider.Dispose();
    }

    /// <summary>A rule whose violations the test sets.</summary>
    private sealed class FakeRule(string name) : IHealthRule
    {
        public IReadOnlyList<HealthViolation> Violations { get; set; } = [];

        public bool Throws { get; set; }

        public Action<HealthCheckContext> OnEvaluate { get; set; } = _ => { };

        public string Name => name;

        public Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            OnEvaluate(context);
            return Throws
                ? throw new InvalidOperationException("The rule broke.")
                : Task.FromResult(Violations);
        }
    }
}
