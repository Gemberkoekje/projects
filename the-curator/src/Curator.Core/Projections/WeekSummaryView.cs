namespace Curator.Core.Projections;

/// <summary>The week's end (BUILD_BRIEF §5.15).</summary>
/// <param name="Money">Money at the end.</param>
/// <param name="Loans">Books lent.</param>
/// <param name="Declines">Visits declined.</param>
/// <param name="WalkOuts">Patrons who walked out.</param>
/// <param name="Heard">Outcomes heard during the week.</param>
/// <param name="LateWord">Word from patrons who never came back, arriving at the week's end.</param>
/// <param name="NeverHeard">Outcomes never heard of.</param>
/// <param name="StoryPatrons">Each story patron's trust.</param>
/// <param name="TutorialBookTitle">The tutorial book.</param>
/// <param name="TutorialBookBack">Whether it came back.</param>
/// <param name="TutorialPagesRemoved">Pages missing from it.</param>
public sealed record WeekSummaryView(
    int Money,
    int Loans,
    int Declines,
    int WalkOuts,
    IReadOnlyList<HeardOutcome> Heard,
    IReadOnlyList<string> LateWord,
    int NeverHeard,
    IReadOnlyList<PatronTrustView> StoryPatrons,
    string TutorialBookTitle,
    bool TutorialBookBack,
    int TutorialPagesRemoved);
