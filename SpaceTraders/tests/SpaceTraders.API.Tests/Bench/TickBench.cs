using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using Xunit;
using Xunit.Abstractions;

namespace SpaceTraders.API.Tests.Bench;

/// <summary>
/// Slice 6.60 (B88, D146): one tick's route work and plan passes, timed on a copy of the day's world. On 2026-11-11 a tick took
/// about 15 minutes on the cluster, one thread computing routes all the time: this is the measure 6.61 is held to.
/// </summary>
/// <remarks>
/// Runs only with <c>SPACETRADERS_BENCH_DB</c> set, to the local copy <c>tools/bench/copy_world.py</c> makes (see
/// <c>tools/bench/README.md</c>); skipped otherwise, as in CI. The plan passes write to the copy as they would to the cluster's
/// database: copy it again for a run that starts from the same world.
/// </remarks>
/// <param name="output">Where the measures go.</param>
[Trait("Category", "Bench")]
public sealed class TickBench(ITestOutputHelper output)
{
    private const string ConnectionStringVariable = "ConnectionStrings__DefaultConnection";

    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [SkippableFact]
    public async Task RouteWorkAndPlanPasses_OnACopyOfTheDaysWorld()
    {
        var connectionString = Environment.GetEnvironmentVariable("SPACETRADERS_BENCH_DB");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "SPACETRADERS_BENCH_DB isn't set: see tools/bench/README.md.");

        // The persistence layer reads the connection string while the services are registered, before a test host's own
        // configuration applies; an environment variable is read by then, and beats appsettings.json, as the soak launcher's.
        var before = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        Environment.SetEnvironmentVariable(ConnectionStringVariable, connectionString);
        try
        {
            await using var factory = new BenchApiFactory(connectionString, ReadChains());
            await ShouldUseOnlyAsync(factory.Services, connectionString);

            // What agent bootstrap does at startup, without the token: the copy's agent is the one whose rows are read.
            factory.Services.GetRequiredService<IAgentDataScope>().Set(await AgentOfAsync(connectionString));

            var traders = await RouteWorkAsync(factory.Services);
            await PlanPassesAsync(factory.Services);

            traders.Should().BePositive("the copy holds a fleet that trades: an empty one measures nothing");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringVariable, before);
        }
    }

    /// <summary>
    /// Before anything reads or writes: the database the host's <c>DbContext</c> connects to is the copy, and no other. The
    /// bench writes to it. Seen on 2026-11-11: a first run reached for the user secrets' database on the cluster's Postgres,
    /// a second for appsettings.json's.
    /// </summary>
    private static async Task ShouldUseOnlyAsync(IServiceProvider root, string connectionString)
    {
        await using var scope = root.CreateAsyncScope();
        var used = new NpgsqlConnectionStringBuilder(scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>().Database.GetConnectionString());
        var wanted = new NpgsqlConnectionStringBuilder(connectionString);
        (used.Host, used.Port, used.Database).Should().Be((wanted.Host, wanted.Port, wanted.Database), "the bench must never run on another database");
    }

    /// <summary>The agent the copy holds (<c>cached_agents</c> has one row an agent).</summary>
    private static async Task<string> AgentOfAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT "AgentId" FROM cached_agents ORDER BY "AgentId" DESC LIMIT 1""", connection);
        var agentId = (string)(await command.ExecuteScalarAsync() ?? string.Empty);
        agentId.Should().NotBeEmpty("the copy holds an agent");
        return agentId;
    }

    /// <summary>
    /// The route work without the plans around it, which writes nothing: each trader's reach map (the trading plan's, D96), and
    /// on it the trader's routes as the role board and the trading plan rank them (<see cref="TradeRoutePlanner.Rank(TradeMarketMap, ShipModel, long, int, IReadOnlySet{string}, HeldBuys)"/>),
    /// and judge them (D76).
    /// </summary>
    /// <returns>The ships that trade.</returns>
    private async Task<int> RouteWorkAsync(IServiceProvider root)
    {
        await using var scope = root.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var fleet = await services.GetRequiredService<IShipRepository>().GetAllAsync(CancellationToken.None);
        var traders = fleet.Where(ship => ship.IsTradingCapable).OrderBy(ship => ship.Symbol, StringComparer.Ordinal).ToList();
        var reader = services.GetRequiredService<ITradeContextReader>();

        var contexts = new Dictionary<string, TradeContext>(StringComparer.OrdinalIgnoreCase);
        var reading = Measure.Start();
        foreach (var system in traders.Select(ship => ship.SystemSymbol ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            contexts[system] = await reader.ReadReachAsync(system, CancellationToken.None);
        }

        var read = reading.Stop();
        var maps = contexts.Values.Select(context => context.Map).ToList();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"World: {fleet.Count} ships, {traders.Count} that trade, in {contexts.Count} systems. Reach maps: {Range(maps, map => map.MarketWaypoints.Count)} markets, {Range(maps, map => map.MarketWaypoints.Count(map.IsFresh))} fresh, {Range(maps, map => map.MarketWaypoints.Count(map.IsPredicted))} predicted."));
        output.WriteLine(read.Line(Invariant($"Reading {contexts.Count} reach maps")));

        // A map plans each flight once (TradeMarketMap.Flights): the first ship on a map plans the hauls the others then reuse.
        var firstOnMap = new List<Measured>();
        var others = new List<Measured>();
        var judging = new List<Measured>();
        var mapsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var routes = 0L;
        var judgements = 0L;
        foreach (var ship in traders)
        {
            var system = ship.SystemSymbol ?? string.Empty;
            var context = contexts[system];
            var rank = Measure.Start();
            routes += TradeRoutePlanner.Rank(context.Map, ship, context.CreditsForCargo, context.MinProfitPerUnit, NoneHeld, HeldBuys.None).Count;
            (mapsSeen.Add(system) ? firstOnMap : others).Add(rank.Stop());
            var judge = Measure.Start();
            judgements += TradeRoutePlanner.Judge(context.Map, ship, context.CreditsForCargo, context.MinProfitPerUnit, NoneHeld, HeldBuys.None).Count;
            judging.Add(judge.Stop());
        }

        output.WriteLine(Measured.Sum([.. firstOnMap, .. others]).Line(Invariant($"Rank, {traders.Count} ships ({routes:N0} lucrative routes)")));
        output.WriteLine(Measured.Sum(firstOnMap).Line(Invariant($"  the first ship on each of {firstOnMap.Count} maps")) + PerShip(firstOnMap));
        output.WriteLine(Measured.Sum(others).Line(Invariant($"  the other {others.Count} ships")) + PerShip(others));
        output.WriteLine(Measured.Sum(judging).Line(Invariant($"Judge, {traders.Count} ships ({judgements:N0} judgements), after Rank on the same maps")) + PerShip(judging));
        return traders.Count;
    }

    /// <summary>Each plan's pass, in the game loop's order and each in its own scope, as <c>GameLoopService</c> runs them.</summary>
    private async Task PlanPassesAsync(IServiceProvider root)
    {
        var total = Measure.Start();
        foreach (var plan in Enum.GetValues<AutomationPlan>())
        {
            await using var scope = root.CreateAsyncScope();
            var services = scope.ServiceProvider;
            if (!await services.GetRequiredService<ISettingsRepository>().IsPlanEnabledAsync(plan, CancellationToken.None))
            {
                output.WriteLine($"{plan,-16} off");
                continue;
            }

            var pass = Measure.Start();
            var outcome = "done";
            try
            {
                await BootstrapAsync(plan, services);
            }
            catch (Exception exception)
            {
                outcome = $"failed: {exception.GetType().Name}: {exception.Message}";
            }

            output.WriteLine(pass.Stop().Line(Invariant($"{plan,-16}")) + "  " + outcome);
        }

        output.WriteLine(total.Stop().Line("All plans"));
    }

    private static Task BootstrapAsync(AutomationPlan plan, IServiceProvider services) => plan switch
    {
        AutomationPlan.Scout => services.GetRequiredService<IScoutAllMarketplacesPlanService>().EnsureBootstrappedAsync(CancellationToken.None),
        AutomationPlan.Explore => services.GetRequiredService<IExplorePlanService>().EnsureBootstrappedAsync(CancellationToken.None),
        AutomationPlan.Roles => services.GetRequiredService<IRolePlanService>().EnsureBootstrappedAsync(CancellationToken.None),
        AutomationPlan.Contract => services.GetRequiredService<IContractPlanService>().EnsureBootstrappedAsync(CancellationToken.None),
        AutomationPlan.ProbeDeployment => services.GetRequiredService<IProbeDeploymentPlanService>().EnsureBootstrappedAsync(CancellationToken.None),
        AutomationPlan.Survey => services.GetRequiredService<ISurveyPlanService>().EnsureBootstrappedAsync(CancellationToken.None),
        AutomationPlan.Mining => services.GetRequiredService<IMiningAutomationService>().EnsureBootstrappedAsync(CancellationToken.None),
        AutomationPlan.Siphon => services.GetRequiredService<ISiphonAutomationService>().EnsureBootstrappedAsync(CancellationToken.None),
        AutomationPlan.Construction => services.GetRequiredService<IConstructionPlanService>().EnsureBootstrappedAsync(CancellationToken.None),
        AutomationPlan.Trading => services.GetRequiredService<ITradingAutomationService>().EnsureBootstrappedAsync(CancellationToken.None),
        AutomationPlan.SpareTime => services.GetRequiredService<ISpareTimePlanService>().EnsureBootstrappedAsync(CancellationToken.None),
        _ => Task.CompletedTask,
    };

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadChains()
    {
        var path = Environment.GetEnvironmentVariable("SPACETRADERS_BENCH_SUPPLY_CHAIN");
        Skip.If(string.IsNullOrWhiteSpace(path) || !File.Exists(path), "SPACETRADERS_BENCH_SUPPLY_CHAIN isn't a file: see tools/bench/README.md.");
        return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path))!
            .ToDictionary(chain => chain.Key, chain => (IReadOnlyList<string>)chain.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Range(IReadOnlyList<TradeMarketMap> maps, Func<TradeMarketMap, int> count)
        => maps.Count == 0 ? "0" : string.Create(CultureInfo.InvariantCulture, $"{maps.Min(count):N0}–{maps.Max(count):N0}");

    private static string PerShip(IReadOnlyList<Measured> measures)
        => measures.Count == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"; a ship: median {Median(measures):N0} ms, most {measures.Max(measure => measure.Milliseconds):N0} ms");

    private static double Median(IReadOnlyList<Measured> measures)
    {
        var sorted = measures.Select(measure => measure.Milliseconds).Order().ToList();
        return sorted[sorted.Count / 2];
    }

    /// <summary>A measure under way: the clock, and the bytes allocated so far.</summary>
    private sealed class Measure
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly long _allocatedAtStart = GC.GetTotalAllocatedBytes(precise: true);

        public static Measure Start() => new();

        public Measured Stop() => new(_clock.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(precise: true) - _allocatedAtStart);
    }

    /// <summary>How long something took and what it allocated.</summary>
    private sealed record Measured(double Milliseconds, long AllocatedBytes)
    {
        public static Measured Sum(IEnumerable<Measured> measures)
            => measures.Aggregate(new Measured(0, 0), (sum, measure) => new Measured(sum.Milliseconds + measure.Milliseconds, sum.AllocatedBytes + measure.AllocatedBytes));

        public string Line(string what)
            => string.Create(CultureInfo.InvariantCulture, $"{what}: {Milliseconds / 1000:N1} s, {AllocatedBytes / 1048576.0:N0} MB allocated");
    }
}
