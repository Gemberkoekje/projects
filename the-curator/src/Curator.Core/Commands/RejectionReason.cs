namespace Curator.Core.Commands;

/// <summary>Why a command was refused. Godot shows these as gentle in-world lines, never as errors.</summary>
public enum RejectionReason
{
    None = 0,
    GameNotStarted,
    GameAlreadyStarted,
    WeekOver,
    DayNotOpen,
    PatronAtCounter,
    NoSlotsLeft,
    NoVisit,
    QuestionUnavailable,
    QuestionAlreadyAsked,
    AlreadyReadThoughts,
    NotEnoughMana,
    UnknownBook,
    BookNotOnShelf,
    NoBookRequested,
    OfferIsRequestedBook,
    VisitIsForced,
    ForcedVisitWaiting,
    NothingToRead,
    UnknownLoan,
    NotTheMoment,
    AlreadyNoted,
    NothingToNote,
    DebugDisabled,
    UnknownCommand,
}
