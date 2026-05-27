namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that replaces one Demon slot with one Townsfolk slot.
/// Used by characters such as Summoner.
/// </summary>
public sealed record ReplaceDemonSetupRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        var adjusted = current with
        {
            Townsfolk = current.Townsfolk + 1,
            Demons = current.Demons - 1,
        };

        return new[] { adjusted };
    }
}
