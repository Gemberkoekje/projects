using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Prometheus;
using SpaceTraders.API.Services;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.API.Tests.Services;

/// <summary>B11: the state the dashboard shows comes from the cache, every 10 seconds.</summary>
public sealed class PrometheusMetricsServiceTests
{
    private const string AgentId = "AGENT@2026-09-27";

    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();

    [Fact]
    public async Task SampleAsync_ExportsCreditsShipsAndContracts_FromTheCache()
    {
        var blocked = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-2", Status = GoalStatus.Blocked, StatusReason = "runaway" };
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Agents.Add(new CachedAgent { AgentId = AgentId, Symbol = "AGENT", StartingFaction = "COSMIC", Credits = 175_000 });
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-1", ShipType = "COMMAND", Status = "DOCKED" });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-2",
                ShipType = "SATELLITE",
                Status = "IN_TRANSIT",
                ArrivesAt = TimeProvider.System.GetUtcNow().AddMinutes(5),
            });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-3",
                ShipType = "EXCAVATOR",
                Status = "IN_TRANSIT",
                ArrivesAt = TimeProvider.System.GetUtcNow().AddMinutes(-1),
            });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-4",
                ShipType = "COMMAND",
                Status = "IN_ORBIT",
                GoalId = blocked.GoalId,
                GoalKind = blocked.Kind.ToString(),
                GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(blocked),
                GoalStatus = (int)GoalStatus.Blocked,
            });
            db.ShipAssignments.Add(new ShipAssignmentRecord { AgentId = AgentId, ShipSymbol = "AGENT-3", Type = "Contract", ContractId = "C-1" });
            db.Contracts.Add(new CachedContract
            {
                AgentId = AgentId,
                Id = "C-1",
                FactionSymbol = "COSMIC",
                Type = "PROCUREMENT",
                IsAccepted = true,
                TermsDeadline = new DateTimeOffset(2026, 10, 08, 07, 09, 22, TimeSpan.Zero),
                DeliverablesJson = JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("IRON_ORE", "X1-AB-2", 42, 7) }),
            });
            db.Contracts.Add(new CachedContract { AgentId = AgentId, Id = "C-2", FactionSymbol = "COSMIC", Type = "PROCUREMENT" });
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<ShipMetricsSample> ships = [];
        IReadOnlyCollection<ContractMetricsSample> contracts = [];
        _metrics.When(m => m.Fleet(Arg.Any<IReadOnlyCollection<ShipMetricsSample>>(), Arg.Any<DateTimeOffset>()))
            .Do(call => ships = call.Arg<IReadOnlyCollection<ShipMetricsSample>>());
        _metrics.When(m => m.Contracts(Arg.Any<IReadOnlyCollection<ContractMetricsSample>>()))
            .Do(call => contracts = call.Arg<IReadOnlyCollection<ContractMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        _metrics.Received(1).Credits(175_000);
        ships.Should().BeEquivalentTo(new[]
        {
            new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty),
            new ShipMetricsSample("AGENT-2", "SATELLITE", "IN_TRANSIT", "None", string.Empty),
            new ShipMetricsSample("AGENT-3", "EXCAVATOR", "IN_ORBIT", "Contract", string.Empty),
            new ShipMetricsSample("AGENT-4", "COMMAND", "IN_ORBIT", "ScoutWaypoint", "runaway"),
        });
        contracts.Should().Equal(new ContractMetricsSample("C-1", "IRON_ORE", 42, 7, new DateTimeOffset(2026, 10, 08, 07, 09, 22, TimeSpan.Zero)));
    }

    private static ServiceProvider BuildProvider()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddSingleton<IAgentDataScope>(_ =>
        {
            var scope = new AgentDataScope();
            scope.Set(AgentId);
            return scope;
        });
        services.AddDbContext<SpaceTradersDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }
}

public sealed class PrometheusAutomationMetricsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 01, 12, 00, 00, TimeSpan.Zero);

    private readonly CollectorRegistry _registry = Metrics.NewCustomRegistry();
    private readonly PrometheusAutomationMetrics _metrics;

    public PrometheusAutomationMetricsTests()
    {
        _metrics = new PrometheusAutomationMetrics(_registry);
    }

    [Fact]
    public async Task AScrape_ListsEveryMetric_BeforeAnyHasAValue()
    {
        var text = await ExportAsync();

        foreach (var name in MetricsEndpointTests.ExpectedMetrics)
        {
            text.Should().Contain($"# TYPE {name} ");
        }
    }

    /// <summary>
    /// Every counter, each with what its first increment looked like on the cluster: the contract's
    /// deposit and the drone, booked before Prometheus first scraped the pod; a single 429; a single
    /// circuit breaker trip.
    /// </summary>
    public static TheoryData<string, Action<IAutomationMetrics>, string> FirstIncrements => new()
    {
        { "credits earned", metrics => metrics.CreditsEarned("ContractDeposit", 4_267), "spacetraders_credits_earned_total{source=\"ContractDeposit\"} " },
        { "credits spent", metrics => metrics.CreditsSpent("ShipPurchase", 46_885), "spacetraders_credits_spent_total{category=\"ShipPurchase\"} " },
        { "429s", metrics => metrics.ApiThrottled("rate_limiter"), "spacetraders_api_throttled_total{source=\"rate_limiter\"} " },
        { "API responses", metrics => metrics.ApiRequest("POST", "my/ships/{shipSymbol}/extract", "429"), "spacetraders_api_requests_total{method=\"POST\",endpoint=\"my/ships/{shipSymbol}/extract\",status=\"429\"} " },
        { "breaker trips", metrics => metrics.GoalBreakerTripped("AGENT-3"), "spacetraders_goal_breaker_trips_total{ship=\"AGENT-3\"} " },
        { "goal steps", metrics => metrics.GoalStep("ScoutWaypoint"), "spacetraders_goal_steps_total{kind=\"ScoutWaypoint\"} " },
        { "messages", metrics => metrics.MessageHandled("ContractAcceptedEvent"), "spacetraders_messages_handled_total{type=\"ContractAcceptedEvent\"} " },
        { "rate-limit waits", metrics => metrics.RateLimitWait(TimeSpan.FromSeconds(3)), "spacetraders_api_rate_limit_wait_seconds_total " },
    };

    /// <summary>
    /// B43: Prometheus's increase() and rate() count what a series gains between two scrapes, never
    /// the value Prometheus first sees. A series that held its first increment when it was first
    /// scraped never showed it on the dashboard: the contract's 4,267 and the drone's 46,885 were
    /// missing from the ledger panels, and a single 429 or breaker trip could never show at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(FirstIncrements))]
    public async Task ACountersFirstIncrement_ReachesPrometheusAfterItHasScrapedTheSeriesAtZero(
        string counter, Action<IAutomationMetrics> increment, string series)
    {
        increment(_metrics);

        (await ExportAsync()).Should().Contain(series + "0", "the first scrape of {0} must see the series at 0", counter);
        (await ExportAsync()).Should().NotContain(series + "0\n", "the scrape after that must see {0}' first increment", counter);
    }

    [Fact]
    public async Task ASeriesThatFirstAppearsLater_IsScrapedAtZeroFirstToo()
    {
        await ExportAsync();
        await ExportAsync();

        _metrics.CreditsSpent("ShipPurchase", 46_885);

        (await ExportAsync()).Should().Contain("spacetraders_credits_spent_total{category=\"ShipPurchase\"} 0\n");
        (await ExportAsync()).Should().Contain("spacetraders_credits_spent_total{category=\"ShipPurchase\"} 46885\n");
    }

    [Fact]
    public async Task ASeriesPrometheusHasSeenAtZero_CountsAtOnce()
    {
        _metrics.CreditsEarned("TradeSell", 450);
        await ExportAsync();
        (await ExportAsync()).Should().Contain("spacetraders_credits_earned_total{source=\"TradeSell\"} 450\n");

        _metrics.CreditsEarned("TradeSell", 50);

        (await ExportAsync()).Should().Contain("spacetraders_credits_earned_total{source=\"TradeSell\"} 500\n");
    }

    [Fact]
    public async Task AShip_KeepsWhenItEnteredItsState_UntilItsStateChanges()
    {
        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty)], Start);
        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty)], Start.AddMinutes(1));

        (await ExportAsync()).Should().Contain(StatusLine("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty, Start));

        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "IN_TRANSIT", "None", string.Empty)], Start.AddMinutes(2));

        var text = await ExportAsync();
        text.Should().Contain(StatusLine("AGENT-1", "COMMAND", "IN_TRANSIT", "None", string.Empty, Start.AddMinutes(2)));
        text.Should().NotContain("state=\"DOCKED\",goal");
        text.Should().Contain("spacetraders_ships{role=\"COMMAND\",state=\"DOCKED\"} 0");
        text.Should().Contain("spacetraders_ships{role=\"COMMAND\",state=\"IN_TRANSIT\"} 1");
    }

    [Fact]
    public async Task AShipThatIsGone_LosesItsSeries()
    {
        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty)], Start);

        _metrics.Fleet([], Start.AddMinutes(1));

        (await ExportAsync()).Should().NotContain("ship=\"AGENT-1\"");
    }

    [Fact]
    public async Task AContractThatIsGone_LosesItsSeries()
    {
        _metrics.Contracts([new ContractMetricsSample("C-1", "IRON_ORE", 42, 7, Start.AddDays(7))]);
        var text = await ExportAsync();
        text.Should().Contain("spacetraders_contract_units_required{contract=\"C-1\",trade_symbol=\"IRON_ORE\"} 42");
        text.Should().Contain("spacetraders_contract_units_fulfilled{contract=\"C-1\",trade_symbol=\"IRON_ORE\"} 7");
        text.Should().Contain($"spacetraders_contract_deadline_timestamp_seconds{{contract=\"C-1\"}} {Start.AddDays(7).ToUnixTimeSeconds()}");

        _metrics.Contracts([]);

        (await ExportAsync()).Should().NotContain("contract=\"C-1\"");
    }

    private static string StatusLine(string ship, string role, string state, string goal, string reason, DateTimeOffset since)
        => $"spacetraders_ship_status_since_timestamp_seconds{{ship=\"{ship}\",role=\"{role}\",state=\"{state}\",goal=\"{goal}\",reason=\"{reason}\"}} {since.ToUnixTimeSeconds()}";

    private async Task<string> ExportAsync()
    {
        using var stream = new MemoryStream();
        await _registry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
