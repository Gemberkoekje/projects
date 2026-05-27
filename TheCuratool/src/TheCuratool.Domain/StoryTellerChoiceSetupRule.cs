namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that offers the Storyteller a choice between multiple alternative rule applications.
/// Each option is applied independently and the union of distinct outcomes is returned.
/// </summary>
/// <param name="Options">The list of <see cref="ISetupRule"/> options the Storyteller may choose from.</param>
public sealed record StoryTellerChoiceSetupRule(IReadOnlyList<ISetupRule> Options) : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        var results = new List<SetupCounts>();

        foreach (var option in Options)
        {
            results.AddRange(option.Apply(current, context));
        }

        return results.Distinct().ToList().AsReadOnly();
    }
}
