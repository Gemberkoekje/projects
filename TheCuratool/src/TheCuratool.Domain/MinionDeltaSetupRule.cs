namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that adjusts the number of Minion slots by a fixed delta.
/// </summary>
/// <param name="Delta">The number of Minion slots to add.</param>
public sealed record MinionDeltaSetupRule(int Delta) : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        var adjusted = current with
        {
            Minions = current.Minions + Delta,
        };

        return new[] { adjusted };
    }
}
