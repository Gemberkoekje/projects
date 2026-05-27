namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that signals a dependency on another character being present in the script
/// (e.g. Huntsman requires Damsel, Choirboy requires King). Does not alter distribution counts directly.
/// </summary>
/// <param name="RequiredId">The canonical character ID that must also be present on the script.</param>
/// <param name="AutoAddIfMissing">When <see langword="true"/>, the required character is injected as out-of-script if absent.</param>
public sealed record RequiresCharacterSetupRule(string RequiredId, bool AutoAddIfMissing = false) : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        return new[] { current };
    }
}
