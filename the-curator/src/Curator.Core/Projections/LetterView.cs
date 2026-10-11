using Curator.Core.Game;

namespace Curator.Core.Projections;

/// <summary>A letter on the desk.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="From">The sender.</param>
/// <param name="Title">A title, or empty.</param>
/// <param name="Text">The letter.</param>
public sealed record LetterView(
    LetterKind Kind,
    string From,
    string Title,
    string Text);
