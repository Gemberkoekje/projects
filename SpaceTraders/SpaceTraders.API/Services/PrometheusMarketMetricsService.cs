using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Infrastructure.Persistence;

namespace SpaceTraders.API.Services;

/// <summary>
/// Every minute, exports the markets and shipyards the bot has cached, for the markets dashboard
/// (slice 2.8): each good's prices, volume, supply and activity, each shipyard's ships and prices,
/// and when each was last refreshed. Markets only change while a ship is there, so a minute is often
/// enough. Once per start it also exports the game's production chains: what each good is made from.
/// They come from <see cref="ISupplyChainCache"/>, which the trading plan shares: one API call per
/// start between them, and after a failure one an hour.
/// <para>
/// It also exports what each known system offers, for the systems dashboard (asked on 2026-10-04,
/// <see cref="SystemOpportunities"/>). A system the command ship has only explored, away from where our ships work, keeps
/// its markets' refresh times and its summary, but not each good's series: exploring has no limit, and at about a
/// thousand series a system they would grow Prometheus without end.
/// </para>
/// </summary>
public sealed class PrometheusMarketMetricsService(
    IServiceScopeFactory serviceScopeFactory,
    IAutomationMetrics metrics,
    ISupplyChainCache supplyChain,
    ILogger<PrometheusMarketMetricsService> logger) : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(1);

    private bool _hasSupplyChain;

    /// <summary>Reads the cache once and hands the markets and shipyards to the metrics.</summary>
    /// <param name="now">The time of this sample; it decides when the production chains are asked for again.</param>
    /// <param name="cancellationToken">Stops the sample.</param>
    internal async Task SampleAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var markets = scope.ServiceProvider.GetRequiredService<IMarketRepository>();
        var shipyards = scope.ServiceProvider.GetRequiredService<IShipyardRepository>();
        var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        var priced = (await markets.GetAllSnapshotsAsync(cancellationToken))
            .ToDictionary(market => market.WaypointSymbol, StringComparer.Ordinal);
        var knownMarkets = await markets.GetAllFreshnessAsync(cancellationToken);
        var knownShipyards = await shipyards.GetAllAsync(cancellationToken);
        var shipyardsObservedAt = (await shipyards.GetAllFreshnessAsync(cancellationToken))
            .ToDictionary(shipyard => shipyard.WaypointSymbol, shipyard => shipyard.LastObservedAt, StringComparer.Ordinal);

        var waypoints = (await db.Waypoints.AsNoTracking()
                .Select(w => new { w.Symbol, w.SystemSymbol, w.Type, w.HasMarket, w.HasShipyard, w.TraitsJson })
                .ToListAsync(cancellationToken))
            .Select(w => new WaypointCacheModel(w.Symbol, w.SystemSymbol, w.Type, 0, 0, w.HasMarket, w.HasShipyard, default, w.TraitsJson))
            .ToList();
        var waypointTypes = waypoints
            .GroupBy(w => w.Symbol, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Type, StringComparer.Ordinal);

        var explore = await scope.ServiceProvider.GetRequiredService<IPlanRepository>().GetAsync<ExplorePlanState>(PlanTypes.Explore, cancellationToken);
        var headquarters = (await scope.ServiceProvider.GetRequiredService<IAgentRepository>().GetAsync(cancellationToken))?.HeadquartersSymbol;
        var home = explore?.HomeSystemSymbol ?? (headquarters is { Length: > 0 } ? WaypointSymbols.SystemOf(headquarters) : string.Empty);
        var business = BusinessSystems.Of(
                await scope.ServiceProvider.GetRequiredService<IShipRepository>().GetAllAsync(cancellationToken),
                BusinessSystems.Explorers(await scope.ServiceProvider.GetRequiredService<IShipAssignmentRepository>().GetAllActiveAsync(cancellationToken)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var summaryOnly = (explore?.Systems ?? [])
            .Where(system => system.ExploredAt is not null
                && !system.SystemSymbol.Equals(home, StringComparison.OrdinalIgnoreCase)
                && !business.Contains(system.SystemSymbol))
            .Select(system => system.SystemSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        metrics.Markets(
        [
            .. knownMarkets.Select(market => new MarketMetricsSample(
                market.SystemSymbol,
                market.WaypointSymbol,
                waypointTypes.GetValueOrDefault(market.WaypointSymbol, string.Empty),
                market.LastObservedAt,
                !summaryOnly.Contains(market.SystemSymbol) && priced.TryGetValue(market.WaypointSymbol, out var snapshot) ? snapshot.TradeGoods : [])),
        ]);
        metrics.Shipyards(
        [
            .. knownShipyards.Select(shipyard => new ShipyardMetricsSample(
                shipyard.SystemSymbol,
                shipyard.WaypointSymbol,
                waypointTypes.GetValueOrDefault(shipyard.WaypointSymbol, string.Empty),
                shipyardsObservedAt.GetValueOrDefault(shipyard.WaypointSymbol),
                shipyard.ShipTypes,
                shipyard.Ships)),
        ]);

        metrics.Systems(SystemOpportunities.Summarise(explore, home, waypoints, [.. priced.Values], now));

        if (!_hasSupplyChain)
        {
            var chains = await supplyChain.GetAsync(scope.ServiceProvider.GetRequiredService<ISpaceTradersPort>(), now, cancellationToken);
            if (chains.Count > 0)
            {
                metrics.SupplyChain(chains);
                _hasSupplyChain = true;
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SampleAsync(TimeProvider.System.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update the market and shipyard metrics.");
            }

            await Task.Delay(SampleInterval, stoppingToken);
        }
    }
}
