namespace TheCuratool.Domain;

/// <summary>
/// Represents a rule that modifies the character distribution during setup.
/// A single rule may produce multiple possible outcomes (e.g. Storyteller-choice rules).
/// </summary>
public interface ISetupRule
{
    /// <summary>
    /// Applies this rule to the given <paramref name="current"/> setup counts and returns all possible resulting distributions.
    /// </summary>
    /// <param name="current">The current <see cref="SetupCounts"/> before this rule is applied.</param>
    /// <param name="context">The <see cref="SetupContext"/> containing player count and chosen character information.</param>
    /// <returns>One or more <see cref="SetupCounts"/> representing valid outcomes after applying this rule.</returns>
    IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context);
}
