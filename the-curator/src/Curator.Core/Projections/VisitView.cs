using Curator.Core.Content;
using Curator.Core.Game;

namespace Curator.Core.Projections;

/// <summary>The visit at the counter (BUILD_BRIEF §4.6, §7.6).</summary>
/// <param name="Present">Whether anyone is at the counter.</param>
/// <param name="VisitId">The visit.</param>
/// <param name="PatronId">The patron.</param>
/// <param name="PatronName">Their name.</param>
/// <param name="PatronDescription">What the curator notices about them.</param>
/// <param name="Gossip">Gossip they told as they arrived.</param>
/// <param name="ReturnTexts">What they said about their earlier loans as they arrived.</param>
/// <param name="Greeting">Their greeting.</param>
/// <param name="RequestText">Their request.</param>
/// <param name="RequestedBookId">The book asked for, or empty.</param>
/// <param name="RequestedBookTitle">Its title, or empty.</param>
/// <param name="RequestedBookOut">Whether the book asked for is out on loan.</param>
/// <param name="BookOutLine">The line telling them so.</param>
/// <param name="Topic">The spine tag of what they asked for.</param>
/// <param name="Questions">Questions on offer.</param>
/// <param name="Conversation">Questions asked and answers heard.</param>
/// <param name="LastLine">Their most recent line.</param>
/// <param name="ReadThoughtsCast">Whether Read Thoughts was cast.</param>
/// <param name="Fragment">What it surfaced, or empty.</param>
/// <param name="Noticed">Whether a warded mind noticed.</param>
/// <param name="NoThoughtsLine">Shown when Read Thoughts found nothing.</param>
/// <param name="ReadThoughtsCost">Its mana cost.</param>
/// <param name="IdentifyCost">Identify's mana cost.</param>
/// <param name="Patience">Their patience.</param>
/// <param name="Impatient">Patience 1 or less.</param>
/// <param name="Tell">The impatience tell.</param>
/// <param name="Forced">Only lending is allowed.</param>
/// <param name="Decided">Whether the decision is made.</param>
/// <param name="Decision">How it ended.</param>
/// <param name="ExitLine">Their exit line.</param>
/// <param name="CanAsk">Whether questions can be asked.</param>
/// <param name="CanReadThoughts">Whether Read Thoughts can be cast now.</param>
/// <param name="CanLendRequested">Whether the requested book can be stamped and lent.</param>
/// <param name="CanOffer">Whether another book can be offered.</param>
/// <param name="CanDecline">Whether the card can be handed back.</param>
/// <param name="OffersRefused">Books they turned down.</param>
/// <param name="NoteLoanId">A loan that can be written into the notebook now, or empty.</param>
/// <param name="CanNoteThought">Whether the fragment can be copied into the notebook now.</param>
public sealed record VisitView(
    bool Present,
    string VisitId,
    string PatronId,
    string PatronName,
    string PatronDescription,
    IReadOnlyList<string> Gossip,
    IReadOnlyList<string> ReturnTexts,
    string Greeting,
    string RequestText,
    string RequestedBookId,
    string RequestedBookTitle,
    bool RequestedBookOut,
    string BookOutLine,
    string Topic,
    IReadOnlyList<QuestionView> Questions,
    IReadOnlyList<AskedQuestion> Conversation,
    string LastLine,
    bool ReadThoughtsCast,
    string Fragment,
    bool Noticed,
    string NoThoughtsLine,
    int ReadThoughtsCost,
    int IdentifyCost,
    int Patience,
    bool Impatient,
    string Tell,
    bool Forced,
    bool Decided,
    DecisionKind Decision,
    string ExitLine,
    bool CanAsk,
    bool CanReadThoughts,
    bool CanLendRequested,
    bool CanOffer,
    bool CanDecline,
    IReadOnlyList<string> OffersRefused,
    string NoteLoanId,
    bool CanNoteThought);
