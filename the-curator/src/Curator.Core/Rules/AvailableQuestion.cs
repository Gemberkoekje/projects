namespace Curator.Core.Rules;

/// <summary>A question the curator can ask in the current visit.</summary>
/// <param name="Id">The question id.</param>
/// <param name="Text">How the curator asks it.</param>
/// <param name="Specific">Whether investigation unlocked it.</param>
/// <param name="Asked">Whether it was asked already this visit.</param>
public sealed record AvailableQuestion(string Id, string Text, bool Specific, bool Asked);
