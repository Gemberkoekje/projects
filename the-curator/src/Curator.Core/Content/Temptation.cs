namespace Curator.Core.Content;

/// <summary>What the patron would do with a spell of this effect tag.</summary>
public sealed record Temptation
{
    public required string Tag { get; init; }

    public required TemptationUse Use { get; init; }
}
