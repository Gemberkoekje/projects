using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Services;

/// <summary>Fetches a market where one of our ships is, and stores its prices.</summary>
public interface IMarketRefresher
{
    /// <summary>
    /// Fetches the market at a waypoint, stores it and publishes <see cref="MarketDataRefreshedEvent"/>, so
    /// the price history records it. The API shows a market's prices only while one of our ships is there:
    /// an answer without prices isn't stored, because it would wipe the prices the cache has.
    /// </summary>
    /// <param name="systemSymbol">The market's system.</param>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <param name="cancellationToken">Stops the refresh.</param>
    /// <returns>True when prices came back and were stored.</returns>
    Task<bool> RefreshAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the market again right after one of our ships bought or sold there (D25), while the ship is
    /// still docked, so the next decisions see what the trade did to the prices. A failure is logged and
    /// doesn't undo the trade.
    /// </summary>
    /// <param name="systemSymbol">The market's system.</param>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <param name="shipSymbol">The ship that traded there.</param>
    /// <param name="cancellationToken">Stops the refresh.</param>
    /// <returns>A task that completes when the market was fetched, or the fetch failed.</returns>
    Task RefreshAfterTradeAsync(string systemSymbol, string waypointSymbol, string shipSymbol, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class MarketRefresher(
    ISpaceTradersPort port,
    IMarketRepository markets,
    IMessageBus bus,
    ILogger<MarketRefresher> logger) : IMarketRefresher
{
    /// <inheritdoc />
    public async Task<bool> RefreshAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken)
    {
        var market = await port.GetMarketAsync(systemSymbol, waypointSymbol, cancellationToken);
        if (string.IsNullOrWhiteSpace(market.TradeGoodsJson))
        {
            return false;
        }

        await markets.UpsertAsync(market, cancellationToken);
        await bus.PublishAsync(new MarketDataRefreshedEvent(new WaypointSymbol(waypointSymbol), market.TradeGoodsJson));
        return true;
    }

    /// <inheritdoc />
    public async Task RefreshAfterTradeAsync(string systemSymbol, string waypointSymbol, string shipSymbol, CancellationToken cancellationToken)
    {
        try
        {
            if (!await RefreshAsync(systemSymbol, waypointSymbol, cancellationToken))
            {
                logger.LogDebug(
                    "MarketRefresher: the market at {WaypointSymbol} came back without prices after ship {ShipSymbol} traded there; kept the cached prices.",
                    waypointSymbol,
                    shipSymbol);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "MarketRefresher: couldn't fetch the market at {WaypointSymbol} again after ship {ShipSymbol} traded there; the market watch will.",
                waypointSymbol,
                shipSymbol);
        }
    }
}
