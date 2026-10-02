using Microsoft.EntityFrameworkCore;
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

        List<string> places = [.. knownMarkets.Select(m => m.WaypointSymbol).Concat(knownShipyards.Select(s => s.WaypointSymbol)).Distinct(StringComparer.Ordinal)];
        var waypointTypes = await db.Waypoints.AsNoTracking()
            .Where(w => places.Contains(w.Symbol))
            .ToDictionaryAsync(w => w.Symbol, w => w.Type, StringComparer.Ordinal, cancellationToken);

        metrics.Markets(
        [
            .. knownMarkets.Select(market => new MarketMetricsSample(
                market.SystemSymbol,
                market.WaypointSymbol,
                waypointTypes.GetValueOrDefault(market.WaypointSymbol, string.Empty),
                market.LastObservedAt,
                priced.TryGetValue(market.WaypointSymbol, out var snapshot) ? snapshot.TradeGoods : [])),
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
