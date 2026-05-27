namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that signals a dependency on another character being present in the script
/// (e.g. Huntsman requires Damsel). Does not alter distribution counts directly.
/// </summary>
/// <param name="RequiredId">The canonical character ID that must also be present on the script.</param>
public sealed record RequiresCharacterSetupRule(string RequiredId) : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        return new[] { current };
    }
}
