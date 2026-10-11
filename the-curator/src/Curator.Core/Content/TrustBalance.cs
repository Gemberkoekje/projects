namespace Curator.Core.Content;

/// <summary>Trust numbers (BUILD_BRIEF §5.5).</summary>
public sealed record TrustBalance
{
    public required int StrangerStart { get; init; }

    public required int AlternativeAccepted { get; init; }

    public required int Declined { get; init; }

    public required int PastPatience { get; init; }

    public required int WardedNoticed { get; init; }
}
