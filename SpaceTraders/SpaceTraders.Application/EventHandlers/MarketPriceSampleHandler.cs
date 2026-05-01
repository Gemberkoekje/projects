using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Persists a price snapshot for every trade good in a market whenever market data is refreshed
/// and notifies dashboard clients.
/// </summary>
public sealed class MarketPriceSampleHandler(
    IMarketPriceSampleRepository marketPriceSamples,
    IDashboardNotifier notifier)
{
    public async Task Handle(MarketDataRefreshedEvent @event, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(@event.TradeGoodsJson))
        {
            return;
        }

        await marketPriceSamples.AppendSamplesAsync(@event.Waypoint.Value, @event.TradeGoodsJson, cancellationToken);
        notifier.Notify("market", @event.Waypoint.Value);
    }
}
