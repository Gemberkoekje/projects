using Curator.Core.Content;
using Curator.Core.Game;
using Curator.Core.Projections;

namespace Curator.Core.Bots;

/// <summary>How a bot's week went.</summary>
/// <param name="Bot">The bot.</param>
/// <param name="Seed">The seed.</param>
/// <param name="Session">The finished session, for replay checks.</param>
/// <param name="Money">Money at the end.</param>
/// <param name="BonusMana">Bonus mana granted over the week.</param>
/// <param name="ManaGranted">All mana granted over the week.</param>
/// <param name="Loans">Books lent.</param>
/// <param name="Declines">Visits declined.</param>
/// <param name="WalkOuts">Patrons who walked out.</param>
/// <param name="OutcomesByCategory">Lend outcomes by category.</param>
/// <param name="StoryTrust">Each story patron's trust at the end.</param>
public sealed record BotResult(
    string Bot,
    long Seed,
    GameSession Session,
    int Money,
    int BonusMana,
    int ManaGranted,
    int Loans,
    int Declines,
    int WalkOuts,
    IReadOnlyDictionary<OutcomeCategory, int> OutcomesByCategory,
    IReadOnlyList<PatronTrustView> StoryTrust)
{
    /// <summary>Harm and mixed outcomes from lending.</summary>
    public int HarmOrMixed =>
        OutcomesByCategory.GetValueOrDefault(OutcomeCategory.Harm) + OutcomesByCategory.GetValueOrDefault(OutcomeCategory.Mixed);

    /// <summary>Whether the week ended in debt.</summary>
    public bool EndedInDebt => Money < 0;
}
