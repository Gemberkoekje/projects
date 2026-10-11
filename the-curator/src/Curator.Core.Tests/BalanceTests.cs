using Curator.Core.Bots;
using Curator.Core.Content;

namespace Curator.Core.Tests;

/// <summary>BUILD_BRIEF §8.4: the strategy bots on the real content, the guardrails and the balance report.</summary>
public sealed class BalanceTests
{
    private static readonly long[] Seeds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    private static readonly Lazy<IReadOnlyList<BotResult>> Results = new(() =>
    {
        var content = ContentLoader.Load(new DirectoryContentSource(RepoPaths.GameContent));
        return Seeds.SelectMany(seed => BotRunner.Bots.Values.Select(bot => BotRunner.Play(content, bot(), seed))).ToList();
    });

    private static readonly Lazy<IReadOnlyList<GuardrailResult>> Checked = new(() =>
    {
        var guardrails = Guardrails.Evaluate(Results.Value);
        var outDir = Path.Combine(RepoPaths.Root, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "balance-report.md"), BalanceReport.Render(Results.Value, guardrails));
        return guardrails;
    });

    [Theory]
    [InlineData(Guardrails.Money)]
    [InlineData(Guardrails.DeclineAllInDebt)]
    [InlineData(Guardrails.LendAllHarm)]
    [InlineData(Guardrails.CarefulLessHarm)]
    [InlineData(Guardrails.BonusMana)]
    public void TheGuardrailHolds(string name)
    {
        var guardrail = Checked.Value.Single(g => g.Name == name);

        Assert.True(guardrail.Holds, $"{guardrail.Name}: {guardrail.Detail}");
    }

    [Fact]
    public void TheBalanceReportShowsEveryGuardrail()
    {
        var report = File.ReadAllText(Path.Combine(RepoPaths.Root, "out", "balance-report.md"));

        Assert.All(Checked.Value, g => Assert.Contains(g.Name, report, StringComparison.Ordinal));
        Assert.Equal(Seeds.Length * 3, Results.Value.Count);
    }
}
