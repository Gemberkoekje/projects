namespace Curator.Core.Projections;

/// <summary>The evening's summary (GDD §4).</summary>
/// <param name="Day">The day.</param>
/// <param name="Date">Its date.</param>
/// <param name="Lent">What went out.</param>
/// <param name="Returned">What came back.</param>
/// <param name="Declined">Patrons turned away.</param>
/// <param name="WalkedOut">Patrons who walked out.</param>
/// <param name="Heard">Outcomes heard today.</param>
/// <param name="Fees">Fees earned.</param>
/// <param name="Upkeep">Upkeep paid.</param>
/// <param name="Money">Money at the end of the day.</param>
/// <param name="HasTomorrow">Whether another day follows.</param>
/// <param name="TomorrowMana">Tomorrow's mana, when another day follows.</param>
public sealed record DaySummaryView(
    int Day,
    string Date,
    IReadOnlyList<SummaryLoan> Lent,
    IReadOnlyList<string> Returned,
    IReadOnlyList<string> Declined,
    IReadOnlyList<string> WalkedOut,
    IReadOnlyList<HeardOutcome> Heard,
    int Fees,
    int Upkeep,
    int Money,
    bool HasTomorrow,
    int TomorrowMana);
