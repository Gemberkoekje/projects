namespace Curator.Core.Content;

/// <summary>What a spell does, as a tag the rules can match.</summary>
public sealed record EffectTag
{
    public required string Id { get; init; }

    public required string About { get; init; }
}
