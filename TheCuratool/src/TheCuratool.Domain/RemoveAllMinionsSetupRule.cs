namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that removes all Minion slots and converts them into Townsfolk slots.
/// </summary>
public sealed record RemoveAllMinionsSetupRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        return new[]
        {
            current with
            {
                Townsfolk = current.Townsfolk + current.Minions,
                Minions = 0,
            },
        };
    }
}
