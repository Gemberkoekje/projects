using Curator.Core.Bots;
using Curator.Core.Game;

namespace Curator.Core.Tests;

public sealed class BotTests
{
    [Theory]
    [InlineData("lend-all")]
    [InlineData("decline-all")]
    [InlineData("careful")]
    public void EachBotPlaysTheWholeWeek(string bot)
    {
        var result = BotRunner.Play(TestWorld.Content, BotRunner.Bots[bot](), seed: 1);

        Assert.Equal(DayStatus.WeekOver, result.Session.State.Status);
        Assert.Equal(bot, result.Bot);
    }

    [Fact]
    public void ABotCanStopAfterSomeDays()
    {
        var result = BotRunner.Play(TestWorld.Content, new LendAllBot(), seed: 1, days: 3);

        Assert.Equal(3, result.Session.State.Day);
        Assert.Equal(DayStatus.Open, result.Session.State.Status);
    }

    [Fact]
    public void TheBotsDisagree()
    {
        var lendAll = BotRunner.Play(TestWorld.Content, new LendAllBot(), seed: 1);
        var declineAll = BotRunner.Play(TestWorld.Content, new DeclineAllBot(), seed: 1);
        var careful = BotRunner.Play(TestWorld.Content, new CarefulBot(), seed: 1);

        Assert.True(lendAll.Loans > careful.Loans || lendAll.HarmOrMixed > careful.HarmOrMixed);
        Assert.Equal(1, declineAll.Loans);
        Assert.True(careful.BonusMana > lendAll.BonusMana);
    }

    [Fact]
    public void TheTestContentBalanceReportIsWritten()
    {
        var results = new List<BotResult>();
        foreach (var seed in new long[] { 1, 2, 3 })
        {
            results.AddRange(BotRunner.Bots.Values.Select(bot => BotRunner.Play(TestWorld.Content, bot(), seed)));
        }

        var guardrails = Guardrails.Check(results.First(r => r.Bot == "lend-all"), results.First(r => r.Bot == "careful"), results.First(r => r.Bot == "decline-all"));
        var report = BalanceReport.Render(results, guardrails);
        var outDir = Path.Combine(RepoPaths.Root, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "balance-report-testcontent.md"), report);

        Assert.Contains("| careful | 1 |", report, StringComparison.Ordinal);
    }
}
