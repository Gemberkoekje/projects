using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

/// <summary>
/// Slice 6.47 (D133): a market lists its recent transactions on every look, so most come again; each is stored once, and the
/// counts are by market since a time.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MarketTradeRepositoryTests : IntegrationTestBase
{
    private static readonly DateTimeOffset At = new(2026, 11, 08, 06, 00, 00, TimeSpan.Zero);

    [SkippableFact]
    public async Task ATransactionListedAgain_IsStoredOnce_AndTheCountsAreByMarketSinceATime()
    {
        var first = new[]
        {
            Trade("X1-UK3-D45", "OTHER-3", At.AddMinutes(-10)),
            Trade("X1-UK3-D45", "OTHER-3", At.AddMinutes(-5)),
        };
        var second = new[]
        {
            Trade("X1-UK3-D45", "OTHER-3", At.AddMinutes(-5)),
            Trade("X1-UK3-D45", "RIVAL-1", At),
        };

        (await RecordAsync(first)).Should().Be(2);
        (await RecordAsync(second)).Should().Be(1, "the one at 05:55 was stored already");
        (await RecordAsync([Trade("X1-UK3-A4", "OTHER-3", At.AddDays(-2))])).Should().Be(1);

        await using var db = CreateFreshContext();
        var counts = await new MarketTradeRepository(db).CountSinceAsync(At.AddHours(-24));

        counts.Should().ContainSingle().Which.Should().Be(new Application.Interfaces.Repositories.MarketTradeCount("X1-UK3", "X1-UK3-D45", 3) { Credits = 3 * 20 * 6_494L });
    }

    /// <summary>
    /// B90, on the cluster on 2026-11-13 and 2026-11-14: two ships looked at one market at the same moment, and each look's
    /// transactions were stored in a scope of their own. Both read which were stored already, found the same new ones, and
    /// inserted them: the second insert failed on IX_market_trades_unique (23505), 11 times overnight, each an Error and an
    /// alert mail, until Wolverine's retry found them stored. Here the other look stores its transactions between this
    /// one's read and its write.
    /// </summary>
    [SkippableFact]
    public async Task RecordAsync_TransactionsAnotherLookStoredMeanwhile_AreStoredOnce()
    {
        await using var other = CreateFreshContext();
        var otherLookStoresFirst = new BeforeInsertInterceptor(
            "market_trades",
            async () => await new MarketTradeRepository(other).RecordAsync(
                "X1-UK3",
                [Trade("X1-UK3-D45", "OTHER-3", At.AddMinutes(-5)), Trade("X1-UK3-D45", "RIVAL-1", At)]));
        await using var db = CreateFreshContext(otherLookStoresFirst);

        var added = await new MarketTradeRepository(db).RecordAsync(
            "X1-UK3",
            [Trade("X1-UK3-D45", "OTHER-3", At.AddMinutes(-10)), Trade("X1-UK3-D45", "OTHER-3", At.AddMinutes(-5)), Trade("X1-UK3-D45", "RIVAL-1", At)]);

        otherLookStoresFirst.HasRun.Should().BeTrue("the other look stored its transactions first");
        added.Should().Be(1, "only the one at 05:50 was new by the time this look wrote");
        await using var fresh = CreateFreshContext();
        (await new MarketTradeRepository(fresh).CountSinceAsync(At.AddHours(-1))).Should().ContainSingle().Which.Trades.Should().Be(3);
    }

    private async Task<int> RecordAsync(IReadOnlyList<MarketTradeModel> trades)
    {
        await using var db = CreateFreshContext();
        return await new MarketTradeRepository(db).RecordAsync("X1-UK3", trades);
    }

    private static MarketTradeModel Trade(string waypoint, string ship, DateTimeOffset at)
        => new(waypoint, ship, "ADVANCED_CIRCUITRY", "PURCHASE", 20, 6_494, at);
}
