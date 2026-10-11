namespace Curator.Core.Content;

/// <summary>Patience numbers (BUILD_BRIEF §5.4).</summary>
public sealed record PatienceBalance
{
    public required int Default { get; init; }

    public required int QuestionCost { get; init; }

    public required int IdentifyCost { get; init; }

    public required int RefusedOfferCost { get; init; }

    public required int ReadThoughtsCost { get; init; }
}
