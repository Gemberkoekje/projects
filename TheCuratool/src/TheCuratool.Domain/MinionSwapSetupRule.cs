namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that converts one Demon slot into a Minion slot.
/// Used by characters that replace the Demon role with a Minion (e.g. Summoner).
/// </summary>
public sealed record MinionSwapSetupRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        var adjusted = current with
        {
            Minions = current.Minions + 1,
            Demons = current.Demons - 1,
        };

        return new[] { adjusted };
    }
}
