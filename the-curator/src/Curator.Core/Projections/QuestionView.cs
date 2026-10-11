namespace Curator.Core.Projections;

/// <summary>A question on offer.</summary>
/// <param name="Id">The question id.</param>
/// <param name="Text">How the curator asks it.</param>
/// <param name="Specific">Unlocked by investigation: shown after the standard ones, marked new.</param>
/// <param name="Asked">Already asked this visit.</param>
public sealed record QuestionView(
    string Id,
    string Text,
    bool Specific,
    bool Asked);
