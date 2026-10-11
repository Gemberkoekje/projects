namespace Curator.Core.Projections;

/// <summary>An outcome in the newspaper.</summary>
/// <param name="Headline">The headline.</param>
/// <param name="Text">The article.</param>
public sealed record HeadlineView(
    string Headline,
    string Text);
