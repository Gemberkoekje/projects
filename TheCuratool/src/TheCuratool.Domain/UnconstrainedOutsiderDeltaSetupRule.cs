namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that shifts Outsider slots by a Storyteller-deferred amount.
/// The rule is intentionally open-ended and is treated as a deferred ST choice in setup math.
/// </summary>
public sealed record UnconstrainedOutsiderDeltaSetupRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        return new[] { current };
    }
}
