using Curator.Core.Content;
using Curator.Core.Events;

namespace Curator.Core.Game;

/// <summary>
/// The game's state: a fold over the event log and nothing else (BUILD_BRIEF §4.1). Only
/// <see cref="Apply"/> changes it, and it needs no content to do so.
/// </summary>
public sealed class GameState
{
    private readonly Dictionary<string, PatronState> patrons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BookState> books = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Loan> loans = new(StringComparer.Ordinal);
    private readonly List<Loan> loanList = [];
    private readonly Dictionary<string, Resolution> resolutions = new(StringComparer.Ordinal);
    private readonly List<Resolution> resolutionList = [];
    private readonly HashSet<string> flags = new(StringComparer.Ordinal);

    public bool Started { get; private set; }

    public long Seed { get; private set; }

    public string ContentVersion { get; private set; } = "";

    public int Day { get; private set; }

    public DayPhase Phase { get; private set; }

    public DayStatus Status { get; private set; }

    public int Mana { get; private set; }

    public int Money { get; private set; }

    /// <summary>The library's hidden reputation.</summary>
    public int Reputation { get; private set; }

    /// <summary>The next of today's slots to resolve.</summary>
    public int NextSlotIndex { get; private set; }

    /// <summary>The visit at the counter (possibly decided), or an empty visit.</summary>
    public VisitState Visit { get; private set; } = VisitState.Nobody();

    /// <summary>Whether a visit is at the counter, decided or not.</summary>
    public bool HasVisit => Visit.VisitId.Length > 0;

    /// <summary>Whether a patron is at the counter and hasn't been answered yet.</summary>
    public bool InVisit => HasVisit && Visit.InProgress;

    /// <summary>The number of events applied: the next event's sequence number.</summary>
    public long EventCount { get; private set; }

    /// <summary>Consecutive evenings that ended in debt.</summary>
    public int DebtStreak { get; private set; }

    /// <summary>Visits investigated before their decision on the last recorded day.</summary>
    public int LastAttentive { get; private set; }

    /// <summary>Visits investigated before their decision today.</summary>
    public int InvestigatedToday { get; private set; }

    /// <summary>Good outcomes surfaced since the last morning grant.</summary>
    public int GoodSurfacedSinceGrant { get; private set; }

    public IReadOnlySet<string> Flags => flags;

    /// <summary>In-game loans, oldest first.</summary>
    public IReadOnlyList<Loan> Loans => loanList;

    /// <summary>Resolved outcomes, oldest first.</summary>
    public IReadOnlyList<Resolution> Resolutions => resolutionList;

    /// <summary>Patrons met so far.</summary>
    public IEnumerable<PatronState> Patrons => patrons.Values;

    /// <summary>Whether the curator has met a patron.</summary>
    /// <param name="patronId">The patron.</param>
    /// <returns>True after their first visit.</returns>
    public bool HasMet(string patronId) => patrons.ContainsKey(patronId);

    /// <summary>A patron the curator has met.</summary>
    /// <param name="patronId">The patron.</param>
    /// <returns>Their state.</returns>
    public PatronState Patron(string patronId) =>
        patrons.TryGetValue(patronId, out var patron) ? patron : throw new KeyNotFoundException($"Patron '{patronId}' hasn't visited.");

    /// <summary>A book's state; books never touched are on the shelf, unread.</summary>
    /// <param name="bookId">The book.</param>
    /// <returns>Its state.</returns>
    public BookState Book(string bookId) => books.TryGetValue(bookId, out var book) ? book : new BookState(bookId);

    /// <summary>A loan by id.</summary>
    /// <param name="loanId">The loan.</param>
    /// <returns>The loan.</returns>
    public Loan Loan(string loanId) =>
        loans.TryGetValue(loanId, out var loan) ? loan : throw new KeyNotFoundException($"No loan '{loanId}'.");

    /// <summary>Whether a loan exists.</summary>
    /// <param name="loanId">The loan.</param>
    /// <returns>True when it does.</returns>
    public bool HasLoan(string loanId) => loans.ContainsKey(loanId);

    /// <summary>A resolved outcome by id.</summary>
    /// <param name="resolutionId">The resolution.</param>
    /// <returns>The resolution.</returns>
    public Resolution Resolution(string resolutionId) =>
        resolutions.TryGetValue(resolutionId, out var resolution) ? resolution : throw new KeyNotFoundException($"No resolution '{resolutionId}'.");

    /// <summary>Rebuilds a state from a log.</summary>
    /// <param name="events">The events, in order.</param>
    /// <returns>The folded state.</returns>
    public static GameState Replay(IEnumerable<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var state = new GameState();
        foreach (var e in events)
        {
            state.Apply(e);
        }

        return state;
    }

    /// <summary>Folds one event into the state.</summary>
    /// <param name="e">The event, whose sequence number must be <see cref="EventCount"/>.</param>
    internal void Apply(GameEvent e)
    {
        if (e.Sequence != EventCount)
        {
            throw new InvalidOperationException($"Event {e.GetType().Name} has sequence {e.Sequence}; expected {EventCount}.");
        }

        switch (e)
        {
            case GameStarted started:
                Started = true;
                Seed = started.Seed;
                ContentVersion = started.ContentVersion;
                Money = started.StartMoney;
                Reputation = started.StartReputation;
                break;
            case DayStarted:
                Day = e.Day;
                Phase = DayPhase.Morning;
                Status = DayStatus.Open;
                NextSlotIndex = 0;
                InvestigatedToday = 0;
                Visit = VisitState.Nobody();
                break;
            case BookReturned returned:
                ApplyReturn(returned);
                break;
            case PageRemoved removed:
                WritableBook(removed.BookId).Remove(removed.PageId);
                break;
            case OutcomeSurfaced surfaced:
                ApplySurfaced(surfaced);
                break;
            case OutcomeRedirected redirected:
                var resolution = Resolution(redirected.ResolutionId);
                resolution.Channel = redirected.Channel;
                resolution.SurfaceDay = redirected.SurfaceDay;
                resolution.Redirected = true;
                break;
            case ManaGranted granted:
                Mana = granted.Total;
                GoodSurfacedSinceGrant = 0;
                break;
            case ManaSpent spent:
                Mana = spent.Remaining;
                break;
            case ManaAdded added:
                Mana = added.Total;
                break;
            case VisitStarted started:
                ApplyVisitStarted(started);
                break;
            case SlotSkipped skipped:
                NextSlotIndex = skipped.SlotIndex + 1;
                break;
            case GossipShared gossip:
                CurrentVisit(e).AddGossip(gossip.Text);
                break;
            case RequestMade request:
                CurrentVisit(e).Greeting = request.Greeting;
                Visit.RequestText = request.RequestText;
                break;
            case QuestionAsked asked:
                CurrentVisit(e).AddAsked(new AskedQuestion(asked.QuestionId, asked.QuestionText, asked.Answer));
                Visit.Investigated = true;
                Visit.LastLine = asked.Answer;
                break;
            case ThoughtsRead thoughts:
                CurrentVisit(e).ReadThoughtsCast = true;
                Visit.Fragment = thoughts.Fragment;
                Visit.Noticed = thoughts.Noticed;
                Visit.AddUnlocks(thoughts.Unlocks);
                Visit.Investigated = true;
                break;
            case PageIdentified identified:
                WritableBook(identified.BookId).Identify(identified.PageId);
                MarkInvestigated();
                break;
            case PageResisted resisted:
                WritableBook(resisted.BookId).Resist(resisted.PageId);
                MarkInvestigated();
                break;
            case IdentifyFailed:
                MarkInvestigated();
                break;
            case PatienceSpent patience:
                CurrentVisit(e).Patience = patience.Remaining;
                break;
            case TrustChanged trust:
                Patron(trust.PatronId).Trust = trust.Trust;
                break;
            case OfferRefused refused:
                CurrentVisit(e).AddRefusal(refused.BookId);
                Visit.LastLine = refused.Line;
                break;
            case BookLent lent:
                ApplyLent(lent);
                break;
            case OutcomeResolved resolved:
                ApplyResolved(resolved);
                break;
            case FlagSet flag:
                flags.Add(flag.Flag);
                break;
            case ReputationChanged reputation:
                Reputation = reputation.Reputation;
                break;
            case MoneyChanged money:
                Money = money.Money;
                break;
            case LedgerEntryNoted noted:
                Loan(noted.LoanId).LedgerNoted = true;
                break;
            case LedgerReturnNoted noted:
                Loan(noted.LoanId).ReturnNoted = true;
                break;
            case ThoughtNoted noted when Visit.VisitId == noted.VisitId:
                Visit.ThoughtNoted = true;
                break;
            case PhaseAdvanced phase:
                Phase = phase.Phase;
                break;
            case AttentivenessRecorded attentive:
                LastAttentive = attentive.InvestigatedVisits;
                break;
            case VisitEnded ended:
                ApplyVisitEnded(ended);
                break;
            case DayEnded:
                Status = DayStatus.Ended;
                Visit = VisitState.Nobody();
                DebtStreak = Money < 0 ? DebtStreak + 1 : 0;
                break;
            case WeekEnded:
                Status = DayStatus.WeekOver;
                break;
            default:
                break;
        }

        EventCount++;
    }

    // Events that only make sense with a patron at the counter; anything else is a corrupt log.
    private VisitState CurrentVisit(GameEvent e) =>
        HasVisit ? Visit : throw new InvalidOperationException($"{e.GetType().Name} #{e.Sequence} needs a visit at the counter.");

    private BookState WritableBook(string bookId)
    {
        if (!books.TryGetValue(bookId, out var book))
        {
            book = new BookState(bookId);
            books.Add(bookId, book);
        }

        return book;
    }

    private void MarkInvestigated()
    {
        if (InVisit)
        {
            Visit.Investigated = true;
        }
    }

    private void ApplyReturn(BookReturned returned)
    {
        var loan = Loan(returned.LoanId);
        loan.Returned = true;
        loan.ReturnedDay = Day;
        var book = WritableBook(returned.BookId);
        book.Status = BookStatus.OnShelf;
        book.CurrentLoanId = "";
        book.LastReturnedDay = Day;
    }

    private void ApplySurfaced(OutcomeSurfaced surfaced)
    {
        var resolution = Resolution(surfaced.ResolutionId);
        resolution.Surfaced = true;
        resolution.SurfacedDay = Day;
        resolution.Channel = surfaced.Channel;
        if (resolution.Category == OutcomeCategory.Good)
        {
            GoodSurfacedSinceGrant++;
        }

        if (surfaced.Channel == OutcomeChannel.Return && HasVisit && Visit.PatronId == resolution.PatronId)
        {
            Visit.AddReturnText(resolution.Text);
        }
    }

    private void ApplyVisitStarted(VisitStarted started)
    {
        if (!patrons.TryGetValue(started.PatronId, out var patron))
        {
            patron = new PatronState(started.PatronId) { FirstVisitDay = Day, FirstMetSequence = started.Sequence };
            patrons.Add(started.PatronId, patron);
        }

        patron.Trust = started.Trust;
        patron.VisitCount++;
        patron.LastVisitDay = Day;
        patron.UseVisit(started.VisitId, started.Step);
        Visit = new VisitState(started.VisitId, started.PatronId)
        {
            SlotIndex = started.SlotIndex,
            SlotId = started.SlotId,
            Day = Day,
            Patience = started.Patience,
            Forced = started.Forced,
            RequestedBookId = started.RequestedBookId,
            Topic = started.Topic,
            FirstVisit = started.FirstVisit,
        };
        NextSlotIndex = started.SlotIndex + 1;
    }

    private void ApplyLent(BookLent lent)
    {
        var loan = new Loan(lent.LoanId, lent.BookId, lent.PatronId, lent.VisitId)
        {
            BorrowedDay = Day,
            DueDay = lent.DueDay,
            Returns = lent.Returns,
            Fee = lent.Fee,
            AsAlternative = lent.AsAlternative,
        };
        loans.Add(loan.LoanId, loan);
        loanList.Add(loan);
        Patron(lent.PatronId).AddLoan(loan.LoanId);
        var book = WritableBook(lent.BookId);
        book.Status = lent.Returns ? BookStatus.Lent : BookStatus.Gone;
        book.CurrentLoanId = loan.LoanId;
        if (HasVisit && Visit.VisitId == lent.VisitId)
        {
            Visit.LoanId = loan.LoanId;
        }
    }

    private void ApplyResolved(OutcomeResolved resolved)
    {
        var resolution = new Resolution(resolved.ResolutionId)
        {
            PatronId = resolved.PatronId,
            VisitId = resolved.VisitId,
            BookId = resolved.BookId,
            LoanId = resolved.LoanId,
            Category = resolved.Category,
            Cause = resolved.Cause,
            OutcomeKey = resolved.OutcomeKey,
            Channel = resolved.Channel,
            OriginalChannel = resolved.Channel,
            ResolvedDay = Day,
            SurfaceDay = resolved.SurfaceDay,
            Headline = resolved.Headline,
            From = resolved.From,
            Text = resolved.Text,
            TrustDelta = resolved.TrustDelta,
            HasReputation = resolved.HasReputation,
            Reputation = resolved.Reputation,
            RemovePageOnReturn = resolved.RemovePageOnReturn,
        };
        resolutions.Add(resolution.ResolutionId, resolution);
        resolutionList.Add(resolution);
        if (resolved.LoanId.Length > 0)
        {
            Patron(resolved.PatronId).LastLendCategory = resolved.Category;
        }
    }

    private void ApplyVisitEnded(VisitEnded ended)
    {
        CurrentVisit(ended).Decided = true;
        Visit.Decision = ended.Decision;
        Visit.ExitLine = ended.ExitLine;
        Visit.LastLine = ended.ExitLine;
        if (ended.Decision != DecisionKind.None)
        {
            Patron(Visit.PatronId).LastDecision = ended.Decision;
        }

        if (Visit.Investigated)
        {
            InvestigatedToday++;
        }
    }
}
