using Curator.Core.Content;

namespace Curator.Core.Projections;

/// <summary>An outcome the curator heard of.</summary>
/// <param name="Day">When it surfaced.</param>
/// <param name="Channel">How.</param>
/// <param name="Headline">A headline, or empty.</param>
/// <param name="Text">What was said.</param>
public sealed record HeardOutcome(
    int Day,
    OutcomeChannel Channel,
    string Headline,
    string Text);
