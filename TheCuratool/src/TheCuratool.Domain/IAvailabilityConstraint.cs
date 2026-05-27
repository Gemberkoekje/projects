namespace TheCuratool.Domain;

/// <summary>
/// Represents a constraint that determines whether a character is available for selection
/// given the current draft state (e.g. Kazali, Summoner, Atheist special cases).
/// </summary>
public interface IAvailabilityConstraint
{
    /// <summary>
    /// Returns <see langword="true"/> if the character is available for selection in the given context.
    /// </summary>
    /// <param name="context">The current <see cref="AvailabilityContext"/> describing what has already been chosen.</param>
    bool IsAvailable(AvailabilityContext context);
}
