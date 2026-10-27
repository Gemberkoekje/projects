using System.Linq;
using ByGalacticAccord.Presentation.Bots;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The bot players (design §20.3, plan S5): scripted strategies that play the world through the session as a player does.
/// They must play without breaking anything, over a soak as well as a short run.
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
