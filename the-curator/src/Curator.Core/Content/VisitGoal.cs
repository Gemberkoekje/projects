namespace Curator.Core.Content;

/// <summary>What the patron actually needs: a spell with one of these effect tags at this power or more.</summary>
public sealed record VisitGoal
{
    public required IReadOnlyList<string> Tags { get; init; }

    public required int MinPower { get; init; }
}
