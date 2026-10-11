namespace Curator.Core.Projections;

/// <summary>The morning paper.</summary>
/// <param name="Delivered">Whether a paper came today.</param>
/// <param name="Masthead">The paper's name.</param>
/// <param name="Date">The date.</param>
/// <param name="Headlines">Outcome stories.</param>
/// <param name="Flavour">Ordinary news.</param>
/// <param name="ToneLine">A line about the library.</param>
public sealed record NewspaperView(
    bool Delivered,
    string Masthead,
    string Date,
    IReadOnlyList<HeadlineView> Headlines,
    IReadOnlyList<string> Flavour,
    string ToneLine);
