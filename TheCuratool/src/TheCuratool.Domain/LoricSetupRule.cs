namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that wraps another rule and only applies it when a specific Loric is active for the session.
/// </summary>
/// <param name="LoricId">The ID of the Loric that must be active for the wrapped rule to apply.</param>
/// <param name="Rule">The underlying <see cref="ISetupRule"/> to delegate to when the Loric is active.</param>
public sealed record LoricSetupRule(string LoricId, ISetupRule Rule) : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        if (!context.ActiveLoricIds.Any(id => string.Equals(id, LoricId, StringComparison.OrdinalIgnoreCase)))
        {
            return new[] { current };
        }

        return Rule.Apply(current, context);
    }
}
