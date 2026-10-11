namespace Curator.Core.Bots;

/// <summary>The balance guardrails of BUILD_BRIEF §8.4.</summary>
public static class Guardrails
{
    /// <summary>Checks the three bots' results against the guardrails.</summary>
    /// <param name="lendAll">The lend-all result.</param>
    /// <param name="careful">The careful result.</param>
    /// <param name="declineAll">The decline-all result.</param>
    /// <returns>A line for each guardrail broken; empty when all hold.</returns>
    public static IReadOnlyList<string> Check(BotResult lendAll, BotResult careful, BotResult declineAll)
    {
        ArgumentNullException.ThrowIfNull(lendAll);
        ArgumentNullException.ThrowIfNull(careful);
        ArgumentNullException.ThrowIfNull(declineAll);
        var broken = new List<string>();
        if (!(lendAll.Money > careful.Money && careful.Money > declineAll.Money))
        {
            broken.Add($"money: want lend-all > careful > decline-all; got {lendAll.Money}, {careful.Money}, {declineAll.Money}");
        }

        if (!declineAll.EndedInDebt)
        {
            broken.Add($"money: decline-all should end in debt; it ends on {declineAll.Money}");
        }

        if (lendAll.HarmOrMixed < 3)
        {
            broken.Add($"outcomes: lend-all should cause 3+ harm or mixed outcomes; it causes {lendAll.HarmOrMixed}");
        }

        if (careful.HarmOrMixed > 1)
        {
            broken.Add($"outcomes: careful should cause at most 1 harm or mixed outcome; it causes {careful.HarmOrMixed}");
        }

        if (careful.BonusMana <= lendAll.BonusMana)
        {
            broken.Add($"mana: careful should earn more bonus mana than lend-all; got {careful.BonusMana} vs {lendAll.BonusMana}");
        }

        return broken;
    }
}
