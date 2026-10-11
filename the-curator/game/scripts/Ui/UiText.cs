using Curator.Core.Content;
using Curator.Core.Game;

namespace Curator.Presentation.Ui;

/// <summary>
/// The interface's own words: button captions, headings and labels. In-world lines (what patrons say,
/// refusals, page notes) come from content; these are the controls around them.
/// </summary>
public static class UiText
{
    public const string Lend = "Stamp and lend";
    public const string Decline = "Hand back the card";
    public const string Offer = "Offer this instead";
    public const string NoteIt = "Note it";
    public const string Noted = "In the notebook";
    public const string Shelve = "Back on the shelf";
    public const string Close = "Put it away";
    public const string BackToDrawer = "Back to the drawer";
    public const string ReadThoughts = "Read Thoughts";
    public const string Bell = "Ring the bell";
    public const string CloseLibrary = "Close for the day";
    public const string Yes = "Close the library";
    public const string No = "Not yet";
    public const string NextMorning = "Next morning";
    public const string WeekEnds = "The week's end";
    public const string Desk = "Look down at the desk";
    public const string Counter = "Look up at the counter";
    public const string Newspaper = "Newspaper";
    public const string Letters = "Letters";
    public const string Tray = "Returns";
    public const string Notebook = "Notebook";
    public const string Catalogue = "Catalogue";
    public const string LibraryCard = "Library card";
    public const string Book = "Book";
    public const string FirstVisit = "First visit";
    public const string Borrowed = "Borrowed";
    public const string Returned = "Returned";
    public const string NoLoans = "No loans yet.";
    public const string Mana = "Mana";
    public const string Money = "Money";
    public const string Patience = "Patience";
    public const string NewQuestion = "new";
    public const string Unread = "not yet read";
    public const string PagesRead = "read";
    public const string OnShelf = "On the shelf";
    public const string BackToday = "Back today";
    public const string Gone = "Gone";
    public const string WatchedLoans = "Watched loans";
    public const string Thoughts = "Thoughts noted";
    public const string FreeNotes = "Notes";
    public const string FreeNotesHint = "Whatever you like. The Board doesn't read it.";
    public const string LedgerNoteHint = "a note…";
    public const string Visits = "Visits";
    public const string Trust = "Trust";
    public const string NoPaper = "No paper today.";
    public const string NoLetters = "No letters today.";
    public const string NothingReturned = "Nothing came back today.";
    public const string ReturnedBy = "returned by";
    public const string PagesTorn = "A page is missing.";
    public const string Lent = "Lent";
    public const string Alternative = "instead of the book asked for";
    public const string TurnedAway = "Turned away";
    public const string WalkedOut = "Walked out";
    public const string Heard = "Heard";
    public const string Fees = "Fees";
    public const string Upkeep = "Upkeep";
    public const string TomorrowMana = "Tomorrow's mana";
    public const string Nothing = "Nothing.";
    public const string Ingredients = "Ingredients";
    public const string Fee = "fee";
    public const string Stamped = "Stamped and lent.";
    public const string NoCard = "No one at the counter.";

    /// <summary>The trust words of GDD §5, by level 0–3.</summary>
    public static readonly string[] TrustWords = ["Guarded", "Cautious", "Candid", "Confiding"];

    /// <summary>The time of day as the HUD writes it.</summary>
    /// <param name="phase">The phase.</param>
    /// <returns>The word.</returns>
    public static string Phase(DayPhase phase) => phase switch
    {
        DayPhase.Morning => "Morning",
        DayPhase.Midday => "Midday",
        DayPhase.Afternoon => "Afternoon",
        DayPhase.Dusk => "Dusk",
        DayPhase.Night => "Night",
        _ => "",
    };

    /// <summary>How an outcome reached the curator, as the summaries write it.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The words.</returns>
    public static string Channel(OutcomeChannel channel) => channel switch
    {
        OutcomeChannel.Newspaper => "In the paper",
        OutcomeChannel.Letter => "By letter",
        OutcomeChannel.Gossip => "Overheard",
        OutcomeChannel.Return => "At the counter",
        _ => "",
    };
}
