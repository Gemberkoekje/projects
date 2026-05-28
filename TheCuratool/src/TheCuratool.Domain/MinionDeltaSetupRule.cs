namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that adjusts the number of Minion slots by a fixed delta.
/// </summary>
/// <param name="Delta">The number of Minion slots to add.</param>
public sealed record MinionDeltaSetupRule(int Delta) : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        var adjustedTownsfolk = current.Townsfolk - Delta;
        var adjustedMinions = current.Minions + Delta;
        if (adjustedTownsfolk < 0 || adjustedMinions < 0)
        {
            return Array.Empty<SetupCounts>();
        }

        var adjusted = current with
        {
            Townsfolk = adjustedTownsfolk,
            Minions = adjustedMinions,
        };

        return new[] { adjusted };
    }
}
