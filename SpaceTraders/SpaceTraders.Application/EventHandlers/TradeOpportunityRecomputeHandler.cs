using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

public sealed class TradeOpportunityRecomputeHandler(
    IMarketRepository markets,
    ITradeAnalyser analyser,
    ITradeOpportunityRepository repository,
    ISettingsRepository settings,
    ILogger<TradeOpportunityRecomputeHandler> logger)
{
    public async Task Handle(MarketDataRefreshedEvent @event, CancellationToken cancellationToken)
        => await RecomputeAsync(cancellationToken);

    public async Task Handle(ShipyardDataRefreshedEvent @event, CancellationToken cancellationToken)
        => await RecomputeAsync(cancellationToken);

    private async Task RecomputeAsync(CancellationToken cancellationToken)
    {
        var minProfit = await settings.GetAsync<int>("Trade.MinProfitPerUnit", cancellationToken);

        var snapshots = await markets.GetAllSnapshotsAsync(cancellationToken);

        if (snapshots.Count == 0)
        {
            logger.LogDebug("No market snapshots available for trade opportunity recompute.");
            return;
        }

        var opportunities = analyser.ComputeOpportunities(snapshots, minProfit);
        await repository.ReplaceAllAsync(opportunities, cancellationToken);

        logger.LogInformation("Recomputed {Count} trade opportunities from {Markets} market(s).",
            opportunities.Count, snapshots.Count);
    }
}
