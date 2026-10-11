namespace Curator.Core.Content;

/// <summary>game/content/tags.json: the vocabulary of spine and effect tags.</summary>
public sealed record TagsFile
{
    public required IReadOnlyList<string> SpineTags { get; init; }

    public required IReadOnlyList<EffectTag> EffectTags { get; init; }

    public required bool Placeholder { get; init; }
}
