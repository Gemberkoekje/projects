namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that shifts characters between Townsfolk and Outsider slots by a fixed delta.
/// Used by characters such as Baron (+2 Outsiders) and Godfather (−1 Outsider).
/// </summary>
/// <param name="Delta">The number of Outsider slots to add (negative to remove). Townsfolk count is adjusted inversely.</param>
/// <param name="IsStorytellerChoice">When <see langword="true"/>, the Storyteller may choose whether to apply the delta or not.</param>
public sealed record OutsiderDeltaSetupRule(int Delta, bool IsStorytellerChoice) : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        var adjusted = current with
        {
            Townsfolk = current.Townsfolk - Delta,
            Outsiders = current.Outsiders + Delta,
        };

        if (!IsStorytellerChoice)
        {
            return new[] { adjusted };
        }

        return new[] { current, adjusted };
    }
}
