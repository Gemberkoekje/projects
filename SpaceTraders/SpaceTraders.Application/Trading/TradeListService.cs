using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// The background worker that builds the <see cref="TradeList"/> again and again (PLAN.md slice 6.63, D147, D148: "Have a
/// pre-set (asynchronously generated) list of high value trades"), on the leader, beside the plan and step loops. Once the
/// home system's jump gate is built (<see cref="HomeGate"/>), each round reads the markets within the trading plan's reach of
/// home, estimates every trade for the fleet's largest hold (<see cref="TradeListBuilder"/>), keeps the high-value ones and
/// hands them to the plans (<see cref="TradeListStore"/>); then waits <c>Trade.ListRebuildSeconds</c>. Before the gate it
/// keeps no list, so every route is worked out with each market's fuel price, as before.
/// </summary>
/// <param name="scopes">A scope a round, for the reads.</param>
/// <param name="leaderElection">Only the leader builds.</param>
/// <param name="store">Where the list goes.</param>
/// <param name="logger">Where each list is logged.</param>
public sealed class TradeListService(
    IServiceScopeFactory scopes,
    ILeaderElection leaderElection,
    TradeListStore store,
    ILogger<TradeListService> logger) : BackgroundService
{
    /// <summary>The setting for how many trades the list keeps of each system's buy markets: a close one for a ship anywhere.</summary>
    public const string PerSystemSetting = "Trade.ListPerSystem";

    /// <summary>The setting for how many of the best trades of all the list keeps besides.</summary>
    public const string SizeSetting = "Trade.ListSize";

    /// <summary>The setting for the seconds between two builds.</summary>
    public const string RebuildSecondsSetting = "Trade.ListRebuildSeconds";

    /// <summary>The setting for how many minutes old a list may be for the plans to read it (<see cref="TradeListStore.FreshAt"/>).</summary>
    public const string MaxAgeMinutesSetting = "Trade.ListMaxAgeMinutes";

    /// <summary>The trades of each system kept when <see cref="PerSystemSetting"/> gives none, as it is seeded.</summary>
    public const int DefaultPerSystem = 25;

    /// <summary>The best trades of all kept when <see cref="SizeSetting"/> gives none, as it is seeded.</summary>
    public const int DefaultSize = 2_000;

    /// <summary>The seconds between builds when <see cref="RebuildSecondsSetting"/> gives none, as it is seeded.</summary>
    public const int DefaultRebuildSeconds = 60;

    /// <summary>The list's age when <see cref="MaxAgeMinutesSetting"/> gives none, as it is seeded.</summary>
    public const int DefaultMaxAgeMinutes = 30;

    /// <summary>How often a build is logged at Information; the others at Debug.</summary>
    private static readonly TimeSpan LoggedEvery = TimeSpan.FromHours(1);

    private DateTimeOffset _loggedAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = TimeSpan.FromSeconds(DefaultRebuildSeconds);
            try
            {
                wait = await BuildAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TradeListService: building the trade list failed; the plans read the last one while it is recent enough, and the next build is in {Seconds} s.", wait.TotalSeconds);
            }

            await Task.Delay(wait, stoppingToken);
        }
    }

    /// <summary>One round: builds a list, or keeps none before the home gate is built.</summary>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>How long to wait before the next round.</returns>
    internal async Task<TimeSpan> BuildAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var settings = services.GetRequiredService<ISettingsRepository>();
        var wait = TimeSpan.FromSeconds(await SettingAsync(settings, RebuildSecondsSetting, DefaultRebuildSeconds, cancellationToken));
        if (!leaderElection.IsLeader || !await settings.IsAutomationEnabledAsync(cancellationToken))
        {
            return wait;
        }

        if (!await services.GetRequiredService<HomeGate>().IsBuiltAsync(cancellationToken))
        {
            // After a reset the new agent's gate isn't built: a list of the old one's markets goes.
            store.Set(TradeList.None);
            logger.LogDebug("TradeListService: the home system's jump gate isn't built; no trade list, every route is worked out.");
            return wait;
        }

        var clock = Stopwatch.StartNew();
        var readAt = TimeProvider.System.GetUtcNow();
        var home = await services.GetRequiredService<IConstructionSites>().HomeSystemAsync(cancellationToken);
        var hold = (await services.GetRequiredService<IShipRepository>().GetAllAsync(cancellationToken))
            .Where(ship => ship.IsTradingCapable)
            .Select(ship => ship.CargoCapacity)
            .DefaultIfEmpty(0)
            .Max();
        var context = await services.GetRequiredService<ITradeContextReader>().ReadReachAsync(home, cancellationToken);
        var read = clock.Elapsed;

        // The trades are worked out as the selection reads them, and only the ones it keeps stay (slice 6.67, B91).
        var perSystem = await SettingAsync(settings, PerSystemSetting, DefaultPerSystem, cancellationToken);
        var size = await SettingAsync(settings, SizeSetting, DefaultSize, cancellationToken);
        var list = TradeListBuilder.Select(
            TradeListBuilder.Estimate(context.Map, hold, TripTime.DefaultEngineSpeed, context.MinProfitPerUnit),
            readAt,
            perSystem,
            size,
            context.Map.Waypoints.Select(waypoint => waypoint.SystemSymbol),
            out var estimated);

        // An empty list would leave every ship without a route: with no trade found (no hold, no fresh market), none is kept.
        store.Set(list.Entries.Count == 0 ? TradeList.None : list);

        var level = readAt - _loggedAt >= LoggedEvery ? LogLevel.Information : LogLevel.Debug;
        _loggedAt = level == LogLevel.Information ? readAt : _loggedAt;
        logger.Log(
            level,
            "TradeListService: {Trades} high-value trades of {Estimated} estimated across {Markets} markets, for a hold of {Hold}; read in {ReadSeconds} s, built in {BuildSeconds} s.",
            list.Entries.Count,
            estimated,
            context.Map.MarketWaypoints.Count,
            hold,
            Math.Round(read.TotalSeconds, 1),
            Math.Round((clock.Elapsed - read).TotalSeconds, 1));
        return wait;
    }

    private static async Task<int> SettingAsync(ISettingsRepository settings, string key, int fallback, CancellationToken cancellationToken)
        => await settings.GetAsync<int>(key, cancellationToken) is var value and > 0 ? value : fallback;
}
