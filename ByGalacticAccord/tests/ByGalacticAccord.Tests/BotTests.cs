using System.Linq;
using ByGalacticAccord.Presentation.Bots;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The bot players (design §20.3, plans S5 and S6): scripted strategies that play the world through the session as a player
/// does. They must play without breaking anything, over a soak as well as a short run; and the market reader, which judges
/// what it knows, must earn more than the board taker (plan S6's verify; S10 asks for 1.5×).
/// </summary>
public sealed class BotTests
{
    [Theory]
    [InlineData(42L)]
    [InlineData(20260619L)]
    public void Both_bots_trade_and_keep_every_invariant_over_a_short_run(long seed)
    {
        BotReport taker = BotRun.Run(new BoardTaker(), seed, TestWorld.FastTicks);
        BotReport reader = BotRun.Run(new MarketReader(), seed, TestWorld.FastTicks);

        Assert.Empty(taker.Violations);
        Assert.Empty(reader.Violations);
        Assert.True(taker.Deals > 50, $"The board taker signed {taker.Deals} deals.");
        Assert.Equal((0, 0), (taker.Posts, taker.Counters)); // it only takes, as posted
        Assert.True(reader.Deals > 50 && reader.Counters > 0, $"The market reader signed {reader.Deals} deals and countered {reader.Counters} times.");

        // With its purchase plans on, it posts its own wanted Sales at producers' doors too, and still breaks nothing.
        BotReport planner = BotRun.Run(new MarketReader { PlansPurchases = true }, seed, TestWorld.FastTicks);
        Assert.Empty(planner.Violations);
        Assert.True(planner.Posts > 0, $"The planning reader posted {planner.Posts} times.");
    }

    /// <summary>The seeds the bots are compared over: one seed's world swings too far with every deal a bot makes to judge by.</summary>
    private static readonly long[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8, 42, 20260619 };

    [Fact]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    public void The_market_reader_beats_the_board_taker()
    {
        var reports = new BotReport[Seeds.Length, 2];
        System.Threading.Tasks.Parallel.For(0, Seeds.Length * 2, i =>
            reports[i / 2, i % 2] = BotRun.Run(i % 2 == 0 ? new BoardTaker() : new MarketReader(), Seeds[i / 2], 200_000));

        decimal Worth(BotReport r) => r.EndCredits.Amount + r.EndEscrow.Amount;
        int wins = Enumerable.Range(0, Seeds.Length).Count(k => Worth(reports[k, 1]) > Worth(reports[k, 0]));
        decimal taker = Enumerable.Range(0, Seeds.Length).Sum(k => Worth(reports[k, 0]));
        decimal reader = Enumerable.Range(0, Seeds.Length).Sum(k => Worth(reports[k, 1]));
        Assert.All(Enumerable.Range(0, Seeds.Length), k => Assert.Empty(reports[k, 0].Violations.Concat(reports[k, 1].Violations)));
        Assert.True(wins >= 7 && reader > taker * 1.05m, $"The market reader won {wins} of {Seeds.Length} seeds and ended with {reader / taker:0.00}× the board taker's credits.");
    }

    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    [InlineData(42L)]
    public void Both_bots_play_a_full_soak_without_breaking_an_invariant(long seed)
    {
        BotReport[] reports = { BotRun.Run(new BoardTaker(), seed, 1_000_000), BotRun.Run(new MarketReader(), seed, 1_000_000) };

        Assert.All(reports, r => Assert.Empty(r.Violations));
        Assert.All(reports, r => Assert.Equal(1_000_000, r.Ticks));
        Assert.All(reports, r => Assert.True(r.EndCredits > r.StartCredits, $"The {r.Bot} ended with {r.EndCredits}."));
        Assert.True(reports.Sum(r => r.Haggles) > 0);
    }
}
