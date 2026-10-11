namespace Curator.Core.Projections;

/// <summary>One line of a visit's transcript, in the order it was said.</summary>
/// <param name="Kind">Who says it, and what kind of line it is.</param>
/// <param name="Text">The line.</param>
public sealed record SpeechLine(
    SpeechKind Kind,
    string Text);
