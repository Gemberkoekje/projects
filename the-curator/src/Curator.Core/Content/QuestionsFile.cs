namespace Curator.Core.Content;

/// <summary>game/content/questions.json: the standard questions, deflections and generic lines.</summary>
public sealed record QuestionsFile
{
    public required IReadOnlyList<StandardQuestion> Standard { get; init; }

    /// <summary>Deflections by trust level, used when a visit has no answer at or below the current trust.</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<string>> Deflections { get; init; }

    public required GenericLines GenericLines { get; init; }

    public required bool Placeholder { get; init; }
}
