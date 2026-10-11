namespace Curator.Core.Projections;

/// <summary>What waits on the desk each morning (BUILD_BRIEF §5.1).</summary>
/// <param name="Newspaper">Today's paper.</param>
/// <param name="Letters">Today's letters.</param>
/// <param name="Returns">Books returned this morning.</param>
public sealed record MorningView(
    NewspaperView Newspaper,
    IReadOnlyList<LetterView> Letters,
    IReadOnlyList<ReturnedBookView> Returns);
