using System.Text.Json.Serialization;

namespace Curator.Core.Events;

/// <summary>
/// A fact appended to the game's log. Every state change is an event, and the state is a fold
/// over the log (BUILD_BRIEF §4.1). Events carry the results of any dice already rolled.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(GameStarted), "gameStarted")]
[JsonDerivedType(typeof(DayStarted), "dayStarted")]
[JsonDerivedType(typeof(BookReturned), "bookReturned")]
[JsonDerivedType(typeof(PageRemoved), "pageRemoved")]
[JsonDerivedType(typeof(OutcomeSurfaced), "outcomeSurfaced")]
[JsonDerivedType(typeof(OutcomeRedirected), "outcomeRedirected")]
[JsonDerivedType(typeof(NewspaperDelivered), "newspaperDelivered")]
[JsonDerivedType(typeof(LetterDelivered), "letterDelivered")]
[JsonDerivedType(typeof(ManaGranted), "manaGranted")]
[JsonDerivedType(typeof(ManaSpent), "manaSpent")]
[JsonDerivedType(typeof(ManaAdded), "manaAdded")]
[JsonDerivedType(typeof(VisitStarted), "visitStarted")]
[JsonDerivedType(typeof(SlotSkipped), "slotSkipped")]
[JsonDerivedType(typeof(GossipShared), "gossipShared")]
[JsonDerivedType(typeof(RequestMade), "requestMade")]
[JsonDerivedType(typeof(QuestionAsked), "questionAsked")]
[JsonDerivedType(typeof(ThoughtsRead), "thoughtsRead")]
[JsonDerivedType(typeof(PageIdentified), "pageIdentified")]
[JsonDerivedType(typeof(PageResisted), "pageResisted")]
[JsonDerivedType(typeof(IdentifyFailed), "identifyFailed")]
[JsonDerivedType(typeof(PatienceSpent), "patienceSpent")]
[JsonDerivedType(typeof(TrustChanged), "trustChanged")]
[JsonDerivedType(typeof(OfferRefused), "offerRefused")]
[JsonDerivedType(typeof(BookLent), "bookLent")]
[JsonDerivedType(typeof(VisitDeclined), "visitDeclined")]
[JsonDerivedType(typeof(PatronWalkedOut), "patronWalkedOut")]
[JsonDerivedType(typeof(OutcomeResolved), "outcomeResolved")]
[JsonDerivedType(typeof(FlagSet), "flagSet")]
[JsonDerivedType(typeof(ReputationChanged), "reputationChanged")]
[JsonDerivedType(typeof(MoneyChanged), "moneyChanged")]
[JsonDerivedType(typeof(LedgerEntryNoted), "ledgerEntryNoted")]
[JsonDerivedType(typeof(LedgerReturnNoted), "ledgerReturnNoted")]
[JsonDerivedType(typeof(ThoughtNoted), "thoughtNoted")]
[JsonDerivedType(typeof(PhaseAdvanced), "phaseAdvanced")]
[JsonDerivedType(typeof(UpkeepPaid), "upkeepPaid")]
[JsonDerivedType(typeof(AttentivenessRecorded), "attentivenessRecorded")]
[JsonDerivedType(typeof(VisitEnded), "visitEnded")]
[JsonDerivedType(typeof(DayEnded), "dayEnded")]
[JsonDerivedType(typeof(WeekEnded), "weekEnded")]
[JsonDerivedType(typeof(DebugCheatUsed), "debugCheatUsed")]
public abstract record GameEvent
{
    /// <summary>The event's position in the log, from 0.</summary>
    public long Sequence { get; init; }

    /// <summary>The day it happened (0 before day 1).</summary>
    public int Day { get; init; }
}
