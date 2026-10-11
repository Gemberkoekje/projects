using System.Globalization;

namespace Curator.Core.Bots;

/// <summary>The balance guardrails of BUILD_BRIEF §8.4, checked across several seeds.</summary>
public static class Guardrails
{
    public const string Money = "money: lend-all > careful > decline-all";
    public const string DeclineAllInDebt = "money: decline-all ends in debt";
    public const string LendAllHarm = "outcomes: lend-all causes 3+ harm or mixed";
    public const string CarefulHarm = "outcomes: careful causes at most 1 harm or mixed";
    public const string CarefulLessHarm = "outcomes: careful causes clearly fewer harm or mixed than lend-all";
    public const string BonusMana = "mana: careful earns more bonus mana than lend-all";

    /// <summary>
    /// The brief's guardrails, plus <see cref="CarefulLessHarm"/>: fewer harm or mixed outcomes than
    /// lend-all in every seed, and at least two fewer on average. It stands in for
    /// <see cref="CarefulHarm"/>, which two random Identifies per book can't meet (docs/QUESTIONS.md Q25).
    /// </summary>
    /// <param name="results">Each bot's result for each seed.</param>
    /// <returns>One result per guardrail.</returns>
    public static IReadOnlyList<GuardrailResult> Evaluate(IReadOnlyList<BotResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var seeds = results.GroupBy(r => r.Seed)
            .Select(g => (Seed: g.Key, LendAll: g.Single(r => r.Bot == "lend-all"), Careful: g.Single(r => r.Bot == "careful"), DeclineAll: g.Single(r => r.Bot == "decline-all")))
            .OrderBy(s => s.Seed)
            .ToList();
        var culture = CultureInfo.InvariantCulture;
        string Range(Func<BotResult, int> value, Func<(long Seed, BotResult LendAll, BotResult Careful, BotResult DeclineAll), BotResult> bot)
        {
            var values = seeds.Select(s => value(bot(s))).ToList();
            return string.Create(culture, $"avg {values.Average():F1} ({values.Min()} to {values.Max()})");
        }

        var lendHarm = seeds.Average(s => s.LendAll.HarmOrMixed);
        var carefulHarm = seeds.Average(s => s.Careful.HarmOrMixed);
        return
        [
            new(Money, seeds.All(s => s.LendAll.Money > s.Careful.Money && s.Careful.Money > s.DeclineAll.Money),
                $"lend-all {Range(r => r.Money, s => s.LendAll)}, careful {Range(r => r.Money, s => s.Careful)}, decline-all {Range(r => r.Money, s => s.DeclineAll)}"),
            new(DeclineAllInDebt, seeds.All(s => s.DeclineAll.EndedInDebt), $"decline-all ends on {Range(r => r.Money, s => s.DeclineAll)}"),
            new(LendAllHarm, seeds.All(s => s.LendAll.HarmOrMixed >= 3), $"lend-all {Range(r => r.HarmOrMixed, s => s.LendAll)}"),
            new(CarefulHarm, seeds.All(s => s.Careful.HarmOrMixed <= 1), $"careful {Range(r => r.HarmOrMixed, s => s.Careful)}"),
            new(CarefulLessHarm, seeds.All(s => s.Careful.HarmOrMixed < s.LendAll.HarmOrMixed) && carefulHarm <= lendHarm - 2,
                string.Create(culture, $"careful avg {carefulHarm:F1} against lend-all avg {lendHarm:F1}")),
            new(BonusMana, seeds.All(s => s.Careful.BonusMana > s.LendAll.BonusMana),
                $"careful {Range(r => r.BonusMana, s => s.Careful)}, lend-all {Range(r => r.BonusMana, s => s.LendAll)}"),
        ];
    }
}
