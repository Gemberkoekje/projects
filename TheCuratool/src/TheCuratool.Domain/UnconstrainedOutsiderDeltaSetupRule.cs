namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that shifts Outsider slots by a Storyteller-deferred amount.
/// The rule is intentionally open-ended and is treated as a deferred ST choice in setup math.
/// </summary>
public sealed record UnconstrainedOutsiderDeltaSetupRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        var maxOutsiders = Math.Min(current.Townsfolk + current.Outsiders, context.OutsiderCharacterCount);
        var minDemons = context.HasAnyDemonChosen ? 1 : 0;
        var outcomes = new List<SetupCounts>();

        for (var outsiders = 0; outsiders <= maxOutsiders; outsiders++)
        {
            for (var demons = minDemons; demons <= current.Demons; demons++)
            {
                outcomes.Add(current with
                {
                    Townsfolk = current.Townsfolk + current.Outsiders - outsiders + current.Demons - demons,
                    Outsiders = outsiders,
                    Demons = demons,
                });
            }
        }

        return outcomes;
    }
}
