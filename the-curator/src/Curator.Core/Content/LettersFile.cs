namespace Curator.Core.Content;

/// <summary>game/content/letters.json: tutorial notes and the Board's debt letters.</summary>
public sealed record LettersFile
{
    public required IReadOnlyList<TutorialLetter> Tutorial { get; init; }

    /// <summary>Debt letters by level: 1, 2 and 3.</summary>
    public required IReadOnlyDictionary<int, LetterTemplate> Debt { get; init; }

    public required bool Placeholder { get; init; }
}
