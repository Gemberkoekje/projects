using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

/// <summary>
/// B96: the dead reckoning reads a market's price samples of the last 8 days (<c>GetWaypointPricesAsync</c>, D131). By the
/// (agent, market, good, time) index it read one row from each of thousands of table pages, and on 2026-11-22, with
/// PostgreSQL's cache cold on the QNAP's NFS volume, a read of X1-RS9-D18B took 29.7 s for 12,336 rows and ran into the
/// 30-second timeout. The read is answered from one index, its rows next to each other in the order asked for.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MarketPriceHistoryIndexTests : IntegrationTestBase
{
    private static readonly DateTimeOffset RunStart = new(2026, 11, 15, 13, 00, 00, TimeSpan.Zero);

    [SkippableFact]
    public async Task AMarketsPriceHistory_IsReadFromTheIndexAlone_InTheOrderAsked()
    {
        // 40 markets, 10 goods each, 50 looks: 20,000 samples, a market's 500 spread over the table as the looks came. The
        // container commits asynchronously (Testcontainers' default); a commit not yet in the WAL keeps VACUUM from marking
        // the pages all-visible, and an index-only scan then looks like it would read the table.
        await ExecuteAsync(
            """
            SET synchronous_commit = on;
            INSERT INTO market_price_samples ("AgentId", "WaypointSymbol", "GoodSymbol", "ObservedAt", "PurchasePrice", "SellPrice", "Supply", "Activity", "TradeVolume")
            SELECT 'INTEGRATION-TEST@2026-09-27', 'X1-AB-' || w, 'GOOD_' || g, timestamptz '2026-11-15 13:00:00+00' + s * interval '10 minutes', 100 + g, 90 + g, 'MODERATE', 'GROWING', 60
            FROM generate_series(1, 50) s, generate_series(1, 40) w, generate_series(1, 10) g;
            """);
        await ExecuteAsync("VACUUM (ANALYZE) market_price_samples");
        var read = new CapturingInterceptor();

        await using (var db = CreateFreshContext(read))
        {
            var samples = await new MarketPriceSampleRepository(db).GetWaypointPricesAsync("X1-AB-7", RunStart, RunStart.AddDays(8));
            samples.Should().HaveCount(500);
        }

        var plan = await ExplainAsync(read);

        plan.Should().Contain("Index Only Scan using \"IX_market_price_samples_AgentId_WaypointSymbol_ObservedAt\"");
        plan.Should().NotContain("Sort", "the index hands the rows over in time order");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var db = CreateFreshContext();
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The plan PostgreSQL makes for the captured command, with its parameters.</summary>
    private async Task<string> ExplainAsync(CapturingInterceptor read)
    {
        await using var db = CreateFreshContext();
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("EXPLAIN " + read.CommandText, connection);
        foreach (var parameter in read.Parameters)
        {
            command.Parameters.Add(parameter.Clone());
        }

        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Keeps the text and the parameters of the last query the context sent.</summary>
    [Mutable]
    private sealed class CapturingInterceptor : DbCommandInterceptor
    {
        public string CommandText { get; private set; } = string.Empty;

        public IReadOnlyList<NpgsqlParameter> Parameters { get; private set; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandText = command.CommandText;
            Parameters = command.Parameters.Cast<NpgsqlParameter>().Select(parameter => parameter.Clone()).ToList();
            return ValueTask.FromResult(result);
        }
    }
}
