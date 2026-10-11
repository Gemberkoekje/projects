namespace Curator.Core.Content;

/// <summary>When a patron takes a book they didn't ask for (BUILD_BRIEF §5.10).</summary>
public sealed record AlternativesBalance
{
    public required bool AcceptSameTopic { get; init; }

    public required int MinTrustForAny { get; init; }
}
