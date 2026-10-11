using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Events;
using Curator.Core.Game;
using Curator.Core.Rules;
using Curator.Core.Text;

namespace Curator.Core.Projections;

/// <summary>Every view Godot renders, projected from the state and the log (BUILD_BRIEF §4.6).</summary>
public sealed class Projector
{
    private readonly GameSession session;

    /// <summary>Projects views of a session.</summary>
    /// <param name="session">The session.</param>
    public Projector(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        this.session = session;
    }

    private ContentSet Content => session.Content;

    private GameState State => session.State;

    private GenericLines Generic => Content.Questions.GenericLines;

    /// <summary>The HUD: mana, money, day, date, phase, and the waiting patron.</summary>
    /// <returns>The view.</returns>
    public HudView Hud()
    {
        var state = State;
        var inVisit = state.InVisit;
        var slotsLeft = state.Status == DayStatus.Open && state.NextSlotIndex < Content.Day(state.Day).Slots.Count;
        return new HudView(
            state.Mana,
            state.Money,
            state.Day,
            Content.WeekDays,
            state.Day > 0 ? Date(state.Day) : "",
            state.Phase,
            inVisit,
            inVisit ? Content.Patron(state.Visit.PatronId).Name : "",
            inVisit ? state.Visit.Patience : 0,
            inVisit ? RequestedTitle(state.Visit) : "",
            inVisit ? state.Visit.Topic : "",
            inVisit && state.Visit.Patience <= 1,
            Generic.Impatient,
            slotsLeft,
            session.Check(new RingBell()) == RejectionReason.None,
            session.Check(new CloseForTheDay()) == RejectionReason.None,
            state.Status == DayStatus.WeekOver);
    }

    /// <summary>The visit at the counter, decided or not.</summary>
    /// <returns>The view; <see cref="VisitView.Present"/> is false when the counter is empty.</returns>
    public VisitView Visit()
    {
        var state = State;
        if (!state.HasVisit)
        {
            return new VisitView(
                false, "", "", "", "", [], [], "", "", "", "", false, "", "", [], [], "", false, "", false,
                Generic.NoThoughts, Content.Balance.Mana.ReadThoughtsCost, Content.Balance.Mana.IdentifyCost, 0, false,
                Generic.Impatient, false, false, DecisionKind.None, "", false, false, false, false, false, [], "", false);
        }

        var visit = state.Visit;
        var patron = Content.Patron(visit.PatronId);
        var requested = visit.RequestedBookId.Length > 0;
        var requestedOut = requested && !state.Book(visit.RequestedBookId).InLibrary;
        var inProgress = visit.InProgress;
        var questions = QuestionRules.Available(Content, state)
            .Select(q => new QuestionView(q.Id, q.Text, q.Specific, q.Asked))
            .ToList();
        var noteLoan = visit.LoanId.Length > 0 && session.Check(new NoteLoan(visit.LoanId)) == RejectionReason.None ? visit.LoanId : "";
        return new VisitView(
            true,
            visit.VisitId,
            patron.Id,
            patron.Name,
            patron.Description,
            visit.Gossip,
            visit.ReturnTexts,
            visit.Greeting,
            visit.RequestText,
            visit.RequestedBookId,
            RequestedTitle(visit),
            requestedOut,
            Generic.BookOut,
            visit.Topic,
            questions,
            visit.Asked,
            visit.LastLine,
            visit.ReadThoughtsCast,
            visit.Fragment,
            visit.Noticed,
            Generic.NoThoughts,
            Content.Balance.Mana.ReadThoughtsCost,
            Content.Balance.Mana.IdentifyCost,
            visit.Patience,
            inProgress && visit.Patience <= 1,
            Generic.Impatient,
            visit.Forced,
            visit.Decided,
            visit.Decision,
            visit.ExitLine,
            inProgress,
            session.Check(new ReadThoughts()) == RejectionReason.None,
            session.Check(new LendRequested()) == RejectionReason.None,
            inProgress && !visit.Forced,
            session.Check(new Decline()) == RejectionReason.None,
            visit.OffersRefused,
            noteLoan,
            session.Check(new NoteThought()) == RejectionReason.None);
    }

    /// <summary>The morning desk: today's paper, letters and returns.</summary>
    /// <returns>The view.</returns>
    public MorningView Morning()
    {
        var today = State.Day;
        var todays = session.Events.Where(e => e.Day == today && today > 0).ToList();
        var paper = todays.OfType<NewspaperDelivered>().LastOrDefault();
        var newspaper = paper is null
            ? new NewspaperView(false, Content.Newspaper.Masthead, Date(Math.Max(today, 1)), [], [], "")
            : new NewspaperView(
                true,
                paper.Masthead,
                Date(today),
                paper.HeadlineResolutionIds.Select(id => State.Resolution(id)).Select(r => new HeadlineView(r.Headline, r.Text)).ToList(),
                paper.Flavour,
                paper.ToneLine);
        var letters = todays.OfType<LetterDelivered>().Select(l => new LetterView(l.Kind, l.From, l.Title, l.Text)).ToList();
        var returns = todays.OfType<BookReturned>()
            .Select(r =>
            {
                var removed = todays.OfType<PageRemoved>().Where(p => p.BookId == r.BookId).Select(p => p.PageId).ToList();
                return new ReturnedBookView(
                    r.LoanId,
                    r.BookId,
                    Content.Book(r.BookId).Title,
                    Content.Patron(r.PatronId).Name,
                    session.Check(new NoteReturn(r.LoanId)) == RejectionReason.None,
                    State.Loan(r.LoanId).ReturnNoted,
                    removed);
            })
            .ToList();
        return new MorningView(newspaper, letters, returns);
    }

    /// <summary>The card-catalogue drawer: one index card per book.</summary>
    /// <returns>The view.</returns>
    public CatalogueView Catalogue()
    {
        var cards = Content.Books.OrderBy(b => b.Title, StringComparer.Ordinal).Select(book =>
        {
            var bookState = State.Book(book.Id);
            var out_ = bookState.Status is BookStatus.Lent or BookStatus.Gone;
            var loan = out_ ? State.Loan(bookState.CurrentLoanId) : null;
            return new CatalogueCard(
                book.Id,
                book.Title,
                book.SpineTag,
                Content.FeeFor(book),
                book.Summary,
                bookState.Identified.Count(p => !bookState.Removed.Contains(p)),
                book.Pages.Count(p => !bookState.Removed.Contains(p.Id)),
                bookState.Status,
                State.Day > 0 && bookState.InLibrary && bookState.LastReturnedDay == State.Day,
                loan is null ? "" : Content.Patron(loan.PatronId).Name,
                loan is null ? "" : Date(loan.BorrowedDay));
        });
        return new CatalogueView(cards.ToList());
    }

    /// <summary>A book open on the desk.</summary>
    /// <param name="bookId">The book.</param>
    /// <returns>The view.</returns>
    public BookView Book(string bookId)
    {
        var book = Content.Book(bookId);
        var bookState = State.Book(bookId);
        var numbered = book.Pages.Select((page, index) => (Page: page, Number: index + 1)).ToList();
        var pages = numbered
            .Where(p => bookState.Identified.Contains(p.Page.Id) && !bookState.Removed.Contains(p.Page.Id))
            .Select(p => new PageView(
                p.Page.Id,
                p.Number,
                PageMarkup.Parse(p.Page.SpellName),
                PageMarkup.Parse(p.Page.Text),
                p.Page.Ingredients.Select(Content.Ingredient).Select(i => new IngredientView(i.Name, i.Known)).ToList()))
            .ToList();
        var unread = numbered.Count(p =>
            !bookState.Removed.Contains(p.Page.Id) && !bookState.Identified.Contains(p.Page.Id) && !bookState.Resisted.Contains(p.Page.Id));
        var isRequested = State.InVisit && State.Visit.RequestedBookId == bookId;
        return new BookView(
            book.Id,
            book.Title,
            book.SpineTag,
            book.Rarity,
            Content.FeeFor(book),
            book.Summary,
            $"book_cover_{book.Id}",
            pages,
            unread,
            numbered.Where(p => bookState.Resisted.Contains(p.Page.Id) && !bookState.Removed.Contains(p.Page.Id)).Select(p => p.Number).ToList(),
            numbered.Where(p => bookState.Removed.Contains(p.Page.Id)).Select(p => p.Number).ToList(),
            bookState.Status,
            session.Check(new Identify(bookId)) == RejectionReason.None,
            Content.Balance.Mana.IdentifyCost,
            State.InVisit,
            isRequested,
            isRequested && session.Check(new LendRequested()) == RejectionReason.None,
            session.Check(new OfferBook(bookId)) == RejectionReason.None);
    }

    /// <summary>A patron's library card.</summary>
    /// <param name="patronId">The patron.</param>
    /// <returns>The view.</returns>
    public LibraryCardView Card(string patronId)
    {
        var patron = Content.Patron(patronId);
        var rows = patron.PreGameLoans
            .OrderBy(l => l.BorrowedDay)
            .Select(l => new CardLoanRow(Content.Book(l.BookId).Title, Date(l.BorrowedDay), Date(l.ReturnedDay)))
            .ToList();
        if (State.HasMet(patronId))
        {
            rows.AddRange(State.Patron(patronId).LoanIds.Select(State.Loan).Select(l =>
                new CardLoanRow(Content.Book(l.BookId).Title, Date(l.BorrowedDay), l.Returned ? Date(l.ReturnedDay) : "")));
        }

        return new LibraryCardView(patron.Id, patron.Name, patron.Description, FirstVisit(patron), rows);
    }

    /// <summary>The notebook's pages: one per patron met, in the order first met.</summary>
    /// <returns>The index.</returns>
    public IReadOnlyList<NotebookEntry> NotebookIndex() =>
        State.Patrons
            .OrderBy(p => p.FirstMetSequence)
            .Select(p => new NotebookEntry(p.Id, Content.Patron(p.Id).Name))
            .ToList();

    /// <summary>The curator's notebook page for a patron.</summary>
    /// <param name="patronId">The patron.</param>
    /// <returns>The view.</returns>
    public NotebookView Notebook(string patronId)
    {
        var patron = Content.Patron(patronId);
        var met = State.HasMet(patronId);
        var ledger = State.Loans
            .Where(l => l.PatronId == patronId && l.LedgerNoted)
            .Select(l => new LedgerRow(
                l.LoanId,
                Content.Book(l.BookId).Title,
                Date(l.BorrowedDay),
                l.ReturnNoted ? Date(l.ReturnedDay) : "",
                session.Notes.LedgerNote(l.LoanId)))
            .ToList();
        var thoughts = session.Events.OfType<ThoughtNoted>()
            .Where(t => t.PatronId == patronId)
            .Select(t => new NotedThoughtView(Date(t.Day), t.Fragment))
            .ToList();
        return new NotebookView(
            patron.Id,
            patron.Name,
            FirstVisit(patron),
            (met ? State.Patron(patronId).VisitCount : 0) + patron.PreGameLoans.Count,
            TrustRules.Current(Content, State, patronId),
            ledger,
            thoughts,
            session.Notes.FreeNotes(patronId));
    }

    /// <summary>The summary of a day that has ended.</summary>
    /// <param name="day">The day.</param>
    /// <returns>The view.</returns>
    public DaySummaryView DaySummary(int day)
    {
        var events = session.Events.Where(e => e.Day == day).ToList();
        var lent = events.OfType<BookLent>()
            .Select(l => new SummaryLoan(Content.Book(l.BookId).Title, Content.Patron(l.PatronId).Name, l.Fee, l.AsAlternative))
            .ToList();
        var returned = events.OfType<BookReturned>().Select(r => Content.Book(r.BookId).Title).ToList();
        var declined = events.OfType<VisitDeclined>().Select(d => Content.Visit(d.VisitId).Patron.Name).ToList();
        var walkedOut = events.OfType<PatronWalkedOut>().Select(w => Content.Visit(w.VisitId).Patron.Name).ToList();
        var heard = events.OfType<OutcomeSurfaced>().Select(Heard).ToList();
        var tomorrow = session.Events.OfType<ManaGranted>().FirstOrDefault(m => m.Day == day + 1);
        return new DaySummaryView(
            day,
            Date(day),
            lent,
            returned,
            declined,
            walkedOut,
            heard,
            events.OfType<MoneyChanged>().Where(m => m.Reason == "fee").Sum(m => m.Delta),
            events.OfType<UpkeepPaid>().Sum(u => u.Amount),
            MoneyAtEndOf(day),
            tomorrow is not null,
            tomorrow?.Total ?? 0);
    }

    /// <summary>The week's end: money, loans, what was heard, and where everyone stands.</summary>
    /// <returns>The view.</returns>
    public WeekSummaryView WeekSummary()
    {
        var late = State.Resolutions
            .Where(r => !r.Surfaced && r.OriginalChannel == OutcomeChannel.Return)
            .Select(r => r.Text)
            .ToList();
        var neverHeard = State.Resolutions.Count(r => !r.Surfaced && r.LoanId.Length > 0 && r.OriginalChannel != OutcomeChannel.Return);
        var storyPatrons = Content.Patrons
            .Where(p => p.Role == PatronRole.Story)
            .Select(p => new PatronTrustView(p.Id, p.Name, State.HasMet(p.Id), TrustRules.Current(Content, State, p.Id)))
            .ToList();
        var tutorialLoan = State.Loans.FirstOrDefault(l => Content.Patron(l.PatronId).Role == PatronRole.Tutorial);
        var tutorialBookId = tutorialLoan?.BookId ??
            Content.Patrons.Where(p => p.Role == PatronRole.Tutorial).SelectMany(p => p.Visits).Select(v => v.Request.BookId).FirstOrDefault() ?? "";
        return new WeekSummaryView(
            State.Money,
            State.Loans.Count,
            session.Events.OfType<VisitDeclined>().Count(),
            session.Events.OfType<PatronWalkedOut>().Count(),
            session.Events.OfType<OutcomeSurfaced>().Select(Heard).ToList(),
            late,
            neverHeard,
            storyPatrons,
            tutorialBookId.Length > 0 ? Content.Book(tutorialBookId).Title : "",
            tutorialLoan is null || tutorialLoan.Returned,
            tutorialBookId.Length > 0 ? State.Book(tutorialBookId).Removed.Count : 0);
    }

    /// <summary>Hidden state for the debug overlay.</summary>
    /// <returns>The view.</returns>
    public DebugView Debug()
    {
        var state = State;
        var goal = "";
        var temptations = "";
        var trust = 0;
        if (state.HasVisit)
        {
            var visit = Content.Visit(state.Visit.VisitId).Visit;
            goal = $"{string.Join("/", visit.Goal.Tags)} (power {visit.Goal.MinPower}+)";
            temptations = string.Join(", ", visit.Temptations.Select(t => $"{t.Tag}: {t.Use}"));
            trust = state.Patron(state.Visit.PatronId).Trust;
        }

        var outcomes = state.Resolutions
            .Select(r => new PendingOutcomeView(r.ResolutionId, r.PatronId, r.BookId, r.Category, r.Cause, r.Channel, r.SurfaceDay, r.Surfaced, r.OutcomeKey))
            .ToList();
        return new DebugView(
            state.Seed,
            state.Day,
            state.Phase,
            state.Reputation,
            ReputationRules.Band(Content.Balance, state.Reputation),
            state.HasVisit ? state.Visit.VisitId : "",
            goal,
            temptations,
            trust,
            state.HasVisit ? state.Visit.Patience : 0,
            outcomes,
            state.Flags.Order(StringComparer.Ordinal).ToList(),
            session.Events.TakeLast(20).Select(e => $"#{e.Sequence} day {e.Day}: {e}").ToList());
    }

    private string Date(int day) => CalendarRules.DateText(Content.Schedule.Calendar, day);

    private string RequestedTitle(VisitState visit) =>
        visit.RequestedBookId.Length > 0 ? Content.Book(visit.RequestedBookId).Title : "";

    private string FirstVisit(Patron patron)
    {
        var days = patron.PreGameLoans.Select(l => l.BorrowedDay).ToList();
        if (State.HasMet(patron.Id))
        {
            days.Add(State.Patron(patron.Id).FirstVisitDay);
        }

        return days.Count == 0 ? "" : Date(days.Min());
    }

    private HeardOutcome Heard(OutcomeSurfaced surfaced)
    {
        var resolution = State.Resolution(surfaced.ResolutionId);
        return new HeardOutcome(surfaced.Day, surfaced.Channel, resolution.Headline, resolution.Text);
    }

    private int MoneyAtEndOf(int day)
    {
        var last = session.Events.OfType<MoneyChanged>().LastOrDefault(m => m.Day <= day);
        return last?.Money ?? session.Events.OfType<GameStarted>().Select(g => g.StartMoney).FirstOrDefault();
    }
}
