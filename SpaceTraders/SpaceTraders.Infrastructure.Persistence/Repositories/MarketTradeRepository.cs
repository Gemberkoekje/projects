using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
/// <remarks>
/// A market lists its recent transactions on every look, so most are seen again: a transaction is the same one when its market,
/// ship, good, type and time are (the table's unique index). Two looks at one market can store at the same moment, each in a
/// scope of its own (B90), so the database skips what is stored already, in the one statement that inserts.
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

        if (db.Database.IsRelational())
        {
            // One statement (B90): two ships looked at one market at the same moment; each look read which transactions were
            // stored, found the same new ones and inserted them, and the second insert failed on IX_market_trades_unique. Now
            // the later one skips the rows the first one inserted.
            var listedWaypoints = trades.Select(trade => trade.WaypointSymbol).ToArray();
            var ships = trades.Select(trade => trade.ShipSymbol).ToArray();
            var goods = trades.Select(trade => trade.TradeSymbol).ToArray();
            var types = trades.Select(trade => trade.Type).ToArray();
            var units = trades.Select(trade => trade.Units).ToArray();
            var prices = trades.Select(trade => trade.PricePerUnit).ToArray();
            var times = trades.Select(trade => trade.Timestamp.ToUniversalTime()).ToArray();
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO market_trades ("AgentId", "SystemSymbol", "WaypointSymbol", "ShipSymbol", "TradeSymbol", "Type", "Units", "PricePerUnit", "Timestamp")
                SELECT {db.AgentId}, {systemSymbol}, listed."WaypointSymbol", listed."ShipSymbol", listed."TradeSymbol", listed."Type", listed."Units", listed."PricePerUnit", listed."Timestamp"
                FROM unnest({listedWaypoints}::text[], {ships}::text[], {goods}::text[], {types}::text[], {units}::integer[], {prices}::bigint[], {times}::timestamptz[])
                    AS listed("WaypointSymbol", "ShipSymbol", "TradeSymbol", "Type", "Units", "PricePerUnit", "Timestamp")
                ON CONFLICT ("AgentId", "WaypointSymbol", "ShipSymbol", "TradeSymbol", "Type", "Timestamp") DO NOTHING;
                """, cancellationToken);
        }

        // The in-memory database of unit tests runs no SQL.
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
                .Select(group => new
                {
                    group.Key.SystemSymbol,
                    group.Key.WaypointSymbol,
                    Trades = group.Count(),
                    Credits = group.Sum(trade => (long)trade.Units * trade.PricePerUnit),
                })
                .ToListAsync(cancellationToken))
            .Select(count => new MarketTradeCount(count.SystemSymbol, count.WaypointSymbol, count.Trades) { Credits = count.Credits })
            .ToList();

    private static string Key(string waypoint, string ship, string good, string type, DateTimeOffset timestamp)
        => string.Join('|', waypoint, ship, good, type, timestamp.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
