using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Events;
using Curator.Core.Rules;

namespace Curator.Core.Game;

/// <summary>
/// Validates one command and produces its events (BUILD_BRIEF §4.2, §5). Each command has a pure
/// check, shared with the views so the buttons they show agree with the rules, and an execution
/// that runs only after the check passes, so a refused command changes nothing.
/// </summary>
internal sealed class CommandProcessor
{
    private const int DebtLevels = 3;
    private const int SaltResistance = 1;
    private const int SaltPage = 2;
    private const int SaltGeneric = 3;

    private readonly ContentSet content;
    private readonly GameState state;
    private readonly bool debugEnabled;
    private readonly EventEmitter emit;

    public CommandProcessor(ContentSet content, GameState state, bool debugEnabled)
    {
        this.content = content;
        this.state = state;
        this.debugEnabled = debugEnabled;
        emit = new EventEmitter(state);
    }

    public IReadOnlyList<GameEvent> Emitted => emit.Emitted;

    private Balance Balance => content.Balance;

    private GenericLines Generic => content.Questions.GenericLines;

    /// <summary>Whether a command would be accepted now, without changing anything.</summary>
    /// <param name="command">The command.</param>
    /// <returns>Why it would be refused, or None.</returns>
    public RejectionReason Check(Command command) => command switch
    {
        NewGame _ => state.Started ? RejectionReason.GameAlreadyStarted : RejectionReason.None,
        RingBell _ => CheckRingBell(),
        AskQuestion c => CheckAskQuestion(c),
        ReadThoughts _ => CheckReadThoughts(),
        Identify c => CheckIdentify(c),
        LendRequested _ => CheckLendRequested(),
        OfferBook c => CheckOfferBook(c),
        Decline _ => CheckDecline(),
        NoteLoan c => CheckNoteLoan(c),
        NoteReturn c => CheckNoteReturn(c),
        NoteThought _ => CheckNoteThought(),
        CloseForTheDay _ => CheckCloseForTheDay(),
        DebugAddMana _ => CheckDebug(),
        DebugRevealBook c => CheckDebug() is var reason and not RejectionReason.None ? reason : content.HasBook(c.BookId) ? RejectionReason.None : RejectionReason.UnknownBook,
        DebugEndDay _ => CheckDebug(),
        _ => RejectionReason.UnknownCommand,
    };

    /// <summary>Checks a command and, if it's allowed, carries it out.</summary>
    /// <param name="command">The command.</param>
    /// <returns>Why it was refused, or None.</returns>
    public RejectionReason Run(Command command)
    {
        var rejection = Check(command);
        if (rejection != RejectionReason.None)
        {
            return rejection;
        }

        switch (command)
        {
            case NewGame c:
                NewGame(c);
                break;
            case RingBell _:
                RingBell();
                break;
            case AskQuestion c:
                AskQuestion(c);
                break;
            case ReadThoughts _:
                ReadThoughts();
                break;
            case Identify c:
                Identify(c);
                break;
            case LendRequested _:
                Lend(state.Visit.RequestedBookId, asAlternative: false, DecisionKind.Lent);
                break;
            case OfferBook c:
                OfferBook(c);
                break;
            case Decline _:
                Decline();
                break;
            case NoteLoan c:
                emit.Emit(new LedgerEntryNoted(c.LoanId));
                break;
            case NoteReturn c:
                NoteReturn(c);
                break;
            case NoteThought _:
                emit.Emit(new ThoughtNoted(state.Visit.PatronId, state.Visit.VisitId, state.Visit.Fragment));
                break;
            case CloseForTheDay _:
                EndDay();
                break;
            case DebugAddMana c:
                emit.Emit(new DebugCheatUsed("addMana", c.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                emit.Emit(new ManaAdded(c.Amount, Math.Max(0, state.Mana + c.Amount)));
                break;
            case DebugRevealBook c:
                DebugRevealBook(c);
                break;
            case DebugEndDay _:
                DebugEndDay();
                break;
            default:
                return RejectionReason.UnknownCommand;
        }

        return RejectionReason.None;
    }

    private static string Line(DecisionLine decision, string fallback) => decision.Line.Length > 0 ? decision.Line : fallback;

    private int Draw(int count, int salt = 0) => Prng.NextInt(state.Seed, emit.NextSequence, count, salt);

    // ----- checks -----------------------------------------------------------------------------

    private RejectionReason CheckDay()
    {
        if (!state.Started)
        {
            return RejectionReason.GameNotStarted;
        }

        if (state.Status == DayStatus.WeekOver)
        {
            return RejectionReason.WeekOver;
        }

        return state.Status == DayStatus.Open ? RejectionReason.None : RejectionReason.DayNotOpen;
    }

    private RejectionReason CheckVisit()
    {
        var day = CheckDay();
        if (day != RejectionReason.None)
        {
            return day;
        }

        return state.InVisit ? RejectionReason.None : RejectionReason.NoVisit;
    }

    private RejectionReason CheckRingBell()
    {
        var day = CheckDay();
        if (day != RejectionReason.None)
        {
            return day;
        }

        if (state.InVisit)
        {
            return RejectionReason.PatronAtCounter;
        }

        return state.NextSlotIndex < content.Day(state.Day).Slots.Count ? RejectionReason.None : RejectionReason.NoSlotsLeft;
    }

    private RejectionReason CheckAskQuestion(AskQuestion command)
    {
        var visit = CheckVisit();
        if (visit != RejectionReason.None)
        {
            return visit;
        }

        var available = QuestionRules.Available(content, state).FirstOrDefault(q => q.Id == command.QuestionId);
        if (available is null)
        {
            return RejectionReason.QuestionUnavailable;
        }

        return available.Asked ? RejectionReason.QuestionAlreadyAsked : RejectionReason.None;
    }

    private RejectionReason CheckReadThoughts()
    {
        var visit = CheckVisit();
        if (visit != RejectionReason.None)
        {
            return visit;
        }

        if (state.Visit.ReadThoughtsCast)
        {
            return RejectionReason.AlreadyReadThoughts;
        }

        return state.Mana >= Balance.Mana.ReadThoughtsCost ? RejectionReason.None : RejectionReason.NotEnoughMana;
    }

    private RejectionReason CheckIdentify(Identify command)
    {
        var day = CheckDay();
        if (day != RejectionReason.None)
        {
            return day;
        }

        if (!content.HasBook(command.BookId))
        {
            return RejectionReason.UnknownBook;
        }

        var bookState = state.Book(command.BookId);
        if (!bookState.InLibrary)
        {
            return RejectionReason.BookNotOnShelf;
        }

        if (content.Book(command.BookId).Pages.All(p => bookState.Removed.Contains(p.Id)))
        {
            return RejectionReason.NothingToRead;
        }

        return state.Mana >= Balance.Mana.IdentifyCost ? RejectionReason.None : RejectionReason.NotEnoughMana;
    }

    private RejectionReason CheckLendRequested()
    {
        var visit = CheckVisit();
        if (visit != RejectionReason.None)
        {
            return visit;
        }

        var bookId = state.Visit.RequestedBookId;
        if (bookId.Length == 0)
        {
            return RejectionReason.NoBookRequested;
        }

        return state.Book(bookId).InLibrary ? RejectionReason.None : RejectionReason.BookNotOnShelf;
    }

    private RejectionReason CheckOfferBook(OfferBook command)
    {
        var visit = CheckVisit();
        if (visit != RejectionReason.None)
        {
            return visit;
        }

        if (state.Visit.Forced)
        {
            return RejectionReason.VisitIsForced;
        }

        if (!content.HasBook(command.BookId))
        {
            return RejectionReason.UnknownBook;
        }

        if (!state.Book(command.BookId).InLibrary)
        {
            return RejectionReason.BookNotOnShelf;
        }

        return command.BookId == state.Visit.RequestedBookId ? RejectionReason.OfferIsRequestedBook : RejectionReason.None;
    }

    private RejectionReason CheckDecline()
    {
        var visit = CheckVisit();
        if (visit != RejectionReason.None)
        {
            return visit;
        }

        return state.Visit.Forced ? RejectionReason.VisitIsForced : RejectionReason.None;
    }

    private RejectionReason CheckNoteLoan(NoteLoan command)
    {
        if (!state.HasLoan(command.LoanId))
        {
            return RejectionReason.UnknownLoan;
        }

        if (state.Status != DayStatus.Open || !state.HasVisit || state.Visit.LoanId != command.LoanId)
        {
            return RejectionReason.NotTheMoment;
        }

        return state.Loan(command.LoanId).LedgerNoted ? RejectionReason.AlreadyNoted : RejectionReason.None;
    }

    private RejectionReason CheckNoteReturn(NoteReturn command)
    {
        if (!state.HasLoan(command.LoanId))
        {
            return RejectionReason.UnknownLoan;
        }

        var loan = state.Loan(command.LoanId);
        if (state.Status != DayStatus.Open || !loan.Returned || loan.ReturnedDay != state.Day)
        {
            return RejectionReason.NotTheMoment;
        }

        return loan.ReturnNoted ? RejectionReason.AlreadyNoted : RejectionReason.None;
    }

    private RejectionReason CheckNoteThought()
    {
        if (state.Status != DayStatus.Open || !state.HasVisit)
        {
            return RejectionReason.NoVisit;
        }

        if (state.Visit.Fragment.Length == 0)
        {
            return RejectionReason.NothingToNote;
        }

        return state.Visit.ThoughtNoted ? RejectionReason.AlreadyNoted : RejectionReason.None;
    }

    private RejectionReason CheckCloseForTheDay()
    {
        var day = CheckDay();
        if (day != RejectionReason.None)
        {
            return day;
        }

        if (state.InVisit)
        {
            return RejectionReason.PatronAtCounter;
        }

        return ForcedVisitWaiting() ? RejectionReason.ForcedVisitWaiting : RejectionReason.None;
    }

    // A forced visit (the clerk's writ) can't be skipped by closing early (A9).
    private bool ForcedVisitWaiting()
    {
        var slots = content.Day(state.Day).Slots;
        for (var i = state.NextSlotIndex; i < slots.Count; i++)
        {
            if (slots[i] == ContentSet.FillerSlot)
            {
                continue;
            }

            var visitId = SchedulingRules.NextVisit(content, state, content.Patron(slots[i]), state.Day);
            if (visitId.Length > 0 && content.Visit(visitId).Visit.Forced)
            {
                return true;
            }
        }

        return false;
    }

    private RejectionReason CheckDebug()
    {
        if (!debugEnabled)
        {
            return RejectionReason.DebugDisabled;
        }

        return CheckDay();
    }

    // ----- execution --------------------------------------------------------------------------

    private void NewGame(NewGame command)
    {
        emit.Emit(new GameStarted(command.Seed, content.Version, Balance.Money.Start, Balance.Reputation.Start));
        StartDay(1);
    }

    private void StartDay(int day)
    {
        emit.Emit(new DayStarted { Day = day });

        foreach (var loan in state.Loans.Where(l => l.Returns && !l.Returned && l.DueDay <= day).ToList())
        {
            emit.Emit(new BookReturned(loan.LoanId, loan.BookId, loan.PatronId));
            if (state.Resolutions.FirstOrDefault(r => r.LoanId == loan.LoanId) is { RemovePageOnReturn.Length: > 0 } resolution &&
                content.Book(loan.BookId).Pages.Any(p => p.Id == resolution.RemovePageOnReturn) &&
                !state.Book(loan.BookId).Removed.Contains(resolution.RemovePageOnReturn))
            {
                emit.Emit(new PageRemoved(loan.BookId, resolution.RemovePageOnReturn));
            }
        }

        var due = state.Resolutions
            .Where(r => !r.Surfaced && r.SurfaceDay <= day && r.Channel is OutcomeChannel.Newspaper or OutcomeChannel.Letter)
            .ToList();
        foreach (var resolution in due)
        {
            Surface(resolution, resolution.Channel);
        }

        var paper = content.Newspaper;
        var band = ReputationRules.Band(Balance, state.Reputation);
        var tones = band switch
        {
            ReputationBand.Wary => paper.ToneLines.Wary,
            ReputationBand.Warm => paper.ToneLines.Warm,
            _ => paper.ToneLines.Neutral,
        };
        var tone = tones.Count > 0 ? tones[Draw(tones.Count)] : "";
        var flavour = paper.Flavour.TryGetValue(day, out var items) ? items : [];
        var headlines = due.Where(r => r.Channel == OutcomeChannel.Newspaper).Select(r => r.ResolutionId).ToList();
        emit.Emit(new NewspaperDelivered(paper.Masthead, headlines, flavour, tone, band));

        foreach (var letter in content.Letters.Tutorial.Where(l => l.Day == day))
        {
            emit.Emit(new LetterDelivered(LetterKind.Tutorial, letter.From, letter.Title, letter.Text, "", 0));
        }

        if (state.DebtStreak > 0)
        {
            var level = Math.Min(state.DebtStreak, DebtLevels);
            var debt = content.Letters.Debt[level];
            emit.Emit(new LetterDelivered(LetterKind.Debt, debt.From, debt.Title, debt.Text, "", level));
        }

        foreach (var resolution in due.Where(r => r.Channel == OutcomeChannel.Letter))
        {
            var from = resolution.From.Length > 0 ? resolution.From : content.Patron(resolution.PatronId).Name;
            emit.Emit(new LetterDelivered(LetterKind.Outcome, from, "", resolution.Text, resolution.ResolutionId, 0));
        }

        var grant = ManaRules.MorningGrant(content, state, day);
        emit.Emit(new ManaGranted(grant.Total, grant.Base, grant.AttentiveBonus, grant.GoodBonus, grant.Rollover, grant.Fixed));
    }

    private void Surface(Resolution resolution, OutcomeChannel channel)
    {
        emit.Emit(new OutcomeSurfaced(resolution.ResolutionId, channel));
        if (resolution.TrustDelta != 0 && state.HasMet(resolution.PatronId))
        {
            ChangeTrust(resolution.PatronId, resolution.TrustDelta, "outcome");
        }

        var reputation = ReputationRules.OnSurface(Balance, resolution, channel);
        if (reputation != 0)
        {
            ChangeReputation(reputation, "outcome");
        }
    }

    private void ChangeTrust(string patronId, int delta, string reason)
    {
        var trust = TrustRules.Clamp(state.Patron(patronId).Trust + delta);
        emit.Emit(new TrustChanged(patronId, delta, trust, reason));
    }

    private void ChangeReputation(int delta, string reason) =>
        emit.Emit(new ReputationChanged(delta, state.Reputation + delta, reason));

    private void SetFlags(IEnumerable<string> flags)
    {
        foreach (var flag in flags.Where(f => !state.Flags.Contains(f)))
        {
            emit.Emit(new FlagSet(flag));
        }
    }

    private void RingBell()
    {
        var slots = content.Day(state.Day).Slots;
        while (state.NextSlotIndex < slots.Count)
        {
            var slotIndex = state.NextSlotIndex;
            var slot = SchedulingRules.Resolve(content, state, state.Day, slotIndex);
            if (slot.SlotPatronId.Length > 0 && !slot.SlotPatronCame)
            {
                RedirectMissedReturns(slot.SlotPatronId);
            }

            if (slot.VisitId.Length > 0)
            {
                StartVisit(content.Visit(slot.VisitId), slotIndex, slot.SlotId);
                return;
            }

            emit.Emit(new SlotSkipped(slotIndex, slot.SlotId));
        }
    }

    // A patron's slot passed without them: their due 'return' outcomes come as letters tomorrow.
    private void RedirectMissedReturns(string patronId)
    {
        var missed = state.Resolutions
            .Where(r => !r.Surfaced && r.Channel == OutcomeChannel.Return && r.PatronId == patronId && r.SurfaceDay <= state.Day)
            .ToList();
        foreach (var resolution in missed)
        {
            emit.Emit(new OutcomeRedirected(resolution.ResolutionId, OutcomeChannel.Letter, state.Day + 1));
        }
    }

    private void StartVisit(VisitRef visitRef, int slotIndex, string slotId)
    {
        var (patron, visit) = (visitRef.Patron, visitRef.Visit);
        var first = !state.HasMet(patron.Id);
        var trust = TrustRules.Current(content, state, patron.Id);
        var patience = visit.Patience ?? Balance.Patience.Default;
        emit.Emit(new VisitStarted(visit.Id, patron.Id, visit.Step, slotIndex, slotId, trust, patience, visit.Forced, visit.Request.BookId, visit.Request.Topic, first));

        var gossip = state.Resolutions
            .Where(r => !r.Surfaced && r.Channel == OutcomeChannel.Gossip && r.SurfaceDay <= state.Day && r.PatronId != patron.Id)
            .ToList();
        foreach (var resolution in gossip)
        {
            emit.Emit(new GossipShared(resolution.ResolutionId, patron.Id, resolution.Text));
            Surface(resolution, OutcomeChannel.Gossip);
        }

        var returns = state.Resolutions
            .Where(r => !r.Surfaced && r.Channel == OutcomeChannel.Return && r.PatronId == patron.Id && r.SurfaceDay <= state.Day)
            .ToList();
        foreach (var resolution in returns)
        {
            Surface(resolution, OutcomeChannel.Return);
        }

        var facts = ConditionRules.FactsFor(content, state, patron.Id);
        var greeting = visit.Greeting.FirstOrDefault(g => ConditionRules.Holds(g.When, facts)) ?? visit.Greeting[^1];
        emit.Emit(new RequestMade(greeting.Text, visit.Request.Text));
    }

    // Pays a patience cost. At zero patience it costs trust instead; at zero trust too, the
    // patron walks out and the action doesn't happen (BUILD_BRIEF §5.4). A forced visit never
    // ends in a walk-out: the writ makes lending the only way out (A9).
    private bool PayPatience(int cost)
    {
        if (cost <= 0)
        {
            return true;
        }

        var visit = state.Visit;
        if (visit.Patience >= cost)
        {
            emit.Emit(new PatienceSpent(cost, visit.Patience - cost));
            return true;
        }

        if (visit.Patience > 0)
        {
            emit.Emit(new PatienceSpent(visit.Patience, 0));
        }

        if (state.Patron(visit.PatronId).Trust <= TrustRules.Lowest)
        {
            if (visit.Forced)
            {
                return true;
            }

            WalkOut();
            return false;
        }

        ChangeTrust(visit.PatronId, Balance.Trust.PastPatience, "pastPatience");
        return true;
    }

    private void WalkOut()
    {
        var visitRef = content.Visit(state.Visit.VisitId);
        var decision = visitRef.Visit.Decisions.WalkedOut;
        emit.Emit(new PatronWalkedOut(visitRef.Visit.Id));
        if (decision.Trust is { } delta and not 0)
        {
            ChangeTrust(visitRef.Patron.Id, delta, "walkedOut");
        }

        SetFlags(decision.SetFlags);
        if (Balance.Reputation.WalkedOut != 0)
        {
            ChangeReputation(Balance.Reputation.WalkedOut, "walkedOut");
        }

        Resolve(visitRef, OutcomeRules.ForNonLend(visitRef.Visit, OutcomeCategory.WalkedOut), "", "");
        EndVisit(DecisionKind.WalkedOut, Line(decision, Generic.WalkedOut));
    }

    private void AskQuestion(AskQuestion command)
    {
        var available = QuestionRules.Available(content, state).First(q => q.Id == command.QuestionId);
        var visit = content.Visit(state.Visit.VisitId).Visit;
        var specific = visit.Questions.FirstOrDefault(q => q.Id == command.QuestionId);
        if (!PayPatience(specific?.PatienceCost ?? Balance.Patience.QuestionCost))
        {
            return;
        }

        var answers = specific?.Answers ??
            (visit.Answers.TryGetValue(command.QuestionId, out var byTrust) ? byTrust : new Dictionary<int, string>());
        var trust = state.Patron(state.Visit.PatronId).Trust;
        var answer = QuestionRules.Answer(content, answers, trust, n => Draw(n));
        emit.Emit(new QuestionAsked(command.QuestionId, available.Text, answer, specific is not null));
        if (specific is not null)
        {
            if (specific.Trust != 0)
            {
                ChangeTrust(state.Visit.PatronId, specific.Trust, "question");
            }

            SetFlags(specific.SetFlags);
        }
    }

    private void ReadThoughts()
    {
        if (!PayPatience(Balance.Patience.ReadThoughtsCost))
        {
            return;
        }

        var cost = Balance.Mana.ReadThoughtsCost;
        emit.Emit(new ManaSpent(cost, state.Mana - cost, "readThoughts"));
        var visitRef = content.Visit(state.Visit.VisitId);
        if (visitRef.Patron.Warded)
        {
            emit.Emit(new ThoughtsRead("", [], Noticed: true));
            ChangeTrust(visitRef.Patron.Id, Balance.Trust.WardedNoticed, "wardedNoticed");
            SetFlags([$"{visitRef.Patron.Id}:noticed"]);
            return;
        }

        var spec = visitRef.Visit.ReadThoughts;
        var trust = state.Patron(visitRef.Patron.Id).Trust;
        var fragment = TrustRules.TryAtOrBelow(spec.ByTrust, trust, out var byTrust) ? byTrust : spec.Default;
        emit.Emit(new ThoughtsRead(fragment, spec.Unlocks, Noticed: false));
    }

    private void Identify(Identify command)
    {
        if (state.InVisit && !PayPatience(Balance.Patience.IdentifyCost))
        {
            return;
        }

        var cost = Balance.Mana.IdentifyCost;
        emit.Emit(new ManaSpent(cost, state.Mana - cost, "identify"));
        var book = content.Book(command.BookId);
        if (book.IdentifyResistance > 0 && Prng.NextDouble(state.Seed, emit.NextSequence, SaltResistance) < book.IdentifyResistance)
        {
            emit.Emit(new IdentifyFailed(book.Id));
            return;
        }

        var bookState = state.Book(book.Id);
        var candidates = book.Pages.Where(p => !bookState.Removed.Contains(p.Id)).ToList();
        var page = candidates[Prng.NextInt(state.Seed, emit.NextSequence, candidates.Count, SaltPage)];
        if (page.Unidentifiable)
        {
            emit.Emit(new PageResisted(book.Id, page.Id));
        }
        else
        {
            emit.Emit(new PageIdentified(book.Id, page.Id, bookState.Identified.Contains(page.Id)));
        }
    }

    private void OfferBook(OfferBook command)
    {
        var visitRef = content.Visit(state.Visit.VisitId);
        var visit = visitRef.Visit;
        if (OfferAccepted(visit, content.Book(command.BookId), state.Patron(visitRef.Patron.Id).Trust))
        {
            var named = visit.Request.NamesBook;
            Lend(command.BookId, asAlternative: named, named ? DecisionKind.Alternative : DecisionKind.Lent);
            return;
        }

        var refusal = visit.Decisions.OfferRefused;
        emit.Emit(new OfferRefused(command.BookId, Line(refusal, Generic.OfferRefused)));
        if (refusal.Trust is { } delta and not 0)
        {
            ChangeTrust(visitRef.Patron.Id, delta, "offerRefused");
        }

        SetFlags(refusal.SetFlags);
        PayPatience(Balance.Patience.RefusedOfferCost);
    }

    private bool OfferAccepted(Visit visit, Book book, int trust)
    {
        var sameTopic = visit.Alternatives.AcceptSameTopic ?? Balance.Alternatives.AcceptSameTopic;
        var minTrustForAny = visit.Alternatives.MinTrustForAny ?? Balance.Alternatives.MinTrustForAny;
        return (sameTopic && book.SpineTag == visit.Request.Topic) || trust >= minTrustForAny;
    }

    private void Lend(string bookId, bool asAlternative, DecisionKind decision)
    {
        var visitRef = content.Visit(state.Visit.VisitId);
        var (patron, visit) = (visitRef.Patron, visitRef.Visit);
        var book = content.Book(bookId);
        var removed = state.Book(bookId).Removed;
        var evaluation = OutcomeRules.Evaluate(book.Pages.Where(p => !removed.Contains(p.Id)), visit.Goal, visit.Temptations);
        var choice = OutcomeRules.ForLend(content, visit, bookId, evaluation, n => Draw(n, SaltGeneric));

        var fee = content.FeeFor(book);
        var dueDay = state.Day + (choice.Outcome.ReturnInDays ?? visit.LoanDays ?? Balance.Loans.DefaultDays);
        var loanId = $"loan-{state.Loans.Count + 1}";
        emit.Emit(new BookLent(loanId, bookId, patron.Id, visit.Id, fee, asAlternative, dueDay, choice.Outcome.BookReturns));
        if (fee != 0)
        {
            emit.Emit(new MoneyChanged(fee, state.Money + fee, "fee"));
        }

        var line = decision == DecisionKind.Alternative ? visit.Decisions.Alternative : visit.Decisions.Lent;
        var delta = decision == DecisionKind.Alternative ? line.Trust ?? Balance.Trust.AlternativeAccepted : line.Trust ?? 0;
        if (delta != 0)
        {
            ChangeTrust(patron.Id, delta, decision == DecisionKind.Alternative ? "alternativeAccepted" : "lent");
        }

        SetFlags(line.SetFlags);
        Resolve(visitRef, choice, bookId, loanId);
        EndVisit(decision, Line(line, decision == DecisionKind.Alternative ? Generic.Alternative : Generic.Lent));
    }

    private void Resolve(VisitRef visitRef, OutcomeChoice choice, string bookId, string loanId)
    {
        var (patron, visit) = (visitRef.Patron, visitRef.Visit);
        var outcome = choice.Outcome;
        var surfaceDay = state.Day + (outcome.DelayDays ?? Balance.Outcomes.DefaultDelayDays[choice.Category]);
        var title = bookId.Length > 0 ? content.Book(bookId).Title : "";
        var resolutionId = $"outcome-{state.Resolutions.Count + 1}";
        emit.Emit(new OutcomeResolved(
            resolutionId,
            patron.Id,
            visit.Id,
            bookId,
            loanId,
            choice.Category,
            choice.Cause,
            choice.Key,
            outcome.Channel,
            surfaceDay,
            OutcomeRules.Fill(outcome.Headline, patron.Name, title),
            OutcomeRules.Fill(outcome.From, patron.Name, title),
            OutcomeRules.Fill(outcome.Text, patron.Name, title),
            outcome.Trust,
            outcome.Reputation is not null,
            outcome.Reputation ?? 0,
            outcome.OnReturn.RemovePage));
        SetFlags(outcome.SetFlags);
        if (outcome.Channel == OutcomeChannel.Return &&
            !SchedulingRules.HasSlotAhead(content, patron.Id, state.Day, state.Visit.SlotIndex, surfaceDay))
        {
            emit.Emit(new OutcomeRedirected(resolutionId, OutcomeChannel.Letter, surfaceDay + 1));
        }
    }

    private void EndVisit(DecisionKind decision, string line)
    {
        emit.Emit(new VisitEnded(state.Visit.VisitId, decision, line));
        emit.Emit(new PhaseAdvanced(PhaseRules.AfterVisit(state.Phase)));
    }

    private void Decline()
    {
        var visitRef = content.Visit(state.Visit.VisitId);
        var decision = visitRef.Visit.Decisions.Declined;
        emit.Emit(new VisitDeclined(visitRef.Visit.Id));
        var delta = decision.Trust ?? Balance.Trust.Declined;
        if (delta != 0)
        {
            ChangeTrust(visitRef.Patron.Id, delta, "declined");
        }

        SetFlags(decision.SetFlags);
        if (!state.Visit.Investigated && Balance.Reputation.UnexplainedDecline != 0)
        {
            ChangeReputation(Balance.Reputation.UnexplainedDecline, "unexplainedDecline");
        }

        Resolve(visitRef, OutcomeRules.ForNonLend(visitRef.Visit, OutcomeCategory.Declined), "", "");
        EndVisit(DecisionKind.Declined, Line(decision, Generic.Declined));
    }

    private void NoteReturn(NoteReturn command)
    {
        if (!state.Loan(command.LoanId).LedgerNoted)
        {
            emit.Emit(new LedgerEntryNoted(command.LoanId));
        }

        emit.Emit(new LedgerReturnNoted(command.LoanId));
    }

    private void EndDay()
    {
        var slots = content.Day(state.Day).Slots;
        for (var i = state.NextSlotIndex; i < slots.Count; i++)
        {
            if (slots[i] != ContentSet.FillerSlot)
            {
                RedirectMissedReturns(slots[i]);
            }
        }

        var upkeep = Balance.Money.UpkeepPerDay;
        emit.Emit(new UpkeepPaid(upkeep));
        if (upkeep != 0)
        {
            emit.Emit(new MoneyChanged(-upkeep, state.Money - upkeep, "upkeep"));
        }

        emit.Emit(new AttentivenessRecorded(state.InvestigatedToday));
        emit.Emit(new DayEnded());
        emit.Emit(new PhaseAdvanced(DayPhase.Night));
        if (state.Day >= content.WeekDays)
        {
            emit.Emit(new WeekEnded());
        }
        else
        {
            StartDay(state.Day + 1);
        }
    }

    private void DebugRevealBook(DebugRevealBook command)
    {
        emit.Emit(new DebugCheatUsed("revealBook", command.BookId));
        var bookState = state.Book(command.BookId);
        foreach (var page in content.Book(command.BookId).Pages.Where(p => !bookState.Removed.Contains(p.Id)))
        {
            if (page.Unidentifiable && !state.Book(command.BookId).Resisted.Contains(page.Id))
            {
                emit.Emit(new PageResisted(command.BookId, page.Id));
            }
            else if (!page.Unidentifiable && !state.Book(command.BookId).Identified.Contains(page.Id))
            {
                emit.Emit(new PageIdentified(command.BookId, page.Id, Repeat: false));
            }
        }
    }

    private void DebugEndDay()
    {
        emit.Emit(new DebugCheatUsed("endDay", ""));
        if (state.InVisit)
        {
            EndVisit(DecisionKind.None, "");
        }

        EndDay();
    }
}
