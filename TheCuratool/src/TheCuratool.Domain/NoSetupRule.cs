namespace TheCuratool.Domain;

/// <summary>
/// A no-op setup rule that returns the current distribution unchanged.
/// Used as a placeholder for characters with no distribution effect.
/// </summary>
public sealed record NoSetupRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        return new[] { current };
    }
}
