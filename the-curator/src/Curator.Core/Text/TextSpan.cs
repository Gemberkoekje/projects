namespace Curator.Core.Text;

/// <summary>A run of page text with one level of legibility.</summary>
/// <param name="Kind">How legible the run is.</param>
/// <param name="Text">The run's text, without markers.</param>
public sealed record TextSpan(SpanKind Kind, string Text);
