using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
/// <remarks>
/// A market lists its recent transactions on every look, so most are seen again: a transaction is the same one when its market,
/// ship, good, type and time are (the table's unique index).
/// </remarks>
public sealed class MarketTradeRepository(SpaceTradersDbContext db) : IMarketTradeRepository
{
    /// <inheritdoc />
    public async Task<int> RecordAsync(string systemSymbol, IReadOnlyList<MarketTradeModel> trades, CancellationToken cancellationToken = default)
    {
        if (trades.Count == 0)
        {
            return 0;
        }

        var waypoints = trades.Select(trade => trade.WaypointSymbol).Distinct().ToList();
        var earliest = trades.Min(trade => trade.Timestamp);
        var stored = (await db.MarketTrades
                .AsNoTracking()
                .Where(trade => waypoints.Contains(trade.WaypointSymbol) && trade.Timestamp >= earliest)
                .Select(trade => new { trade.WaypointSymbol, trade.ShipSymbol, trade.TradeSymbol, trade.Type, trade.Timestamp })
                .ToListAsync(cancellationToken))
            .Select(trade => Key(trade.WaypointSymbol, trade.ShipSymbol, trade.TradeSymbol, trade.Type, trade.Timestamp))
            .ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var trade in trades)
        {
            if (!stored.Add(Key(trade.WaypointSymbol, trade.ShipSymbol, trade.TradeSymbol, trade.Type, trade.Timestamp)))
            {
                continue;
            }

            db.MarketTrades.Add(new MarketTrade
            {
                AgentId = db.AgentId,
                SystemSymbol = systemSymbol,
                WaypointSymbol = trade.WaypointSymbol,
                ShipSymbol = trade.ShipSymbol,
                TradeSymbol = trade.TradeSymbol,
                Type = trade.Type,
                Units = trade.Units,
                PricePerUnit = trade.PricePerUnit,
                Timestamp = trade.Timestamp,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return added;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketTradeCount>> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        => (await db.MarketTrades
                .AsNoTracking()
                .Where(trade => trade.Timestamp >= since)
                .GroupBy(trade => new { trade.SystemSymbol, trade.WaypointSymbol })
                .Select(group => new { group.Key.SystemSymbol, group.Key.WaypointSymbol, Trades = group.Count() })
                .ToListAsync(cancellationToken))
            .Select(count => new MarketTradeCount(count.SystemSymbol, count.WaypointSymbol, count.Trades))
            .ToList();

    private static string Key(string waypoint, string ship, string good, string type, DateTimeOffset timestamp)
        => string.Join('|', waypoint, ship, good, type, timestamp.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
