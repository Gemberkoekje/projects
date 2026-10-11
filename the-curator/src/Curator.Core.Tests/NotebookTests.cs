using Curator.Core.Commands;
using Curator.Core.Events;
using Curator.Core.Game;

namespace Curator.Core.Tests;

public sealed class NotebookTests
{
    [Fact]
    public void ALoanCanBeNotedAtTheMomentItIsMade()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");
        session.Do(new LendRequested());

        Assert.Equal("loan-1", session.Views.Visit().NoteLoanId);
        session.Do(new NoteLoan("loan-1"));

        var row = Assert.Single(session.Views.Notebook("board-clerk").Ledger);
        Assert.Equal(("Common Wards & Small Mendings", "3 Seedmonth", ""), (row.BookTitle, row.BorrowedDate, row.ReturnedDate));
        Assert.Equal("", session.Views.Visit().NoteLoanId);
        session.Refused(new NoteLoan("loan-1"), RejectionReason.AlreadyNoted);
    }

    [Fact]
    public void TheMomentPassesWhenTheNextPatronIsCalled()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.Ring("elara-voss-1");
        session.Do(new LendRequested());
        var loan = session.Last<BookLent>().LoanId;

        session.Do(new RingBell());

        session.Refused(new NoteLoan(loan), RejectionReason.NotTheMoment);
        session.Refused(new NoteLoan("loan-99"), RejectionReason.UnknownLoan);
    }

    [Fact]
    public void AReturnCanBeNotedOnTheDayItComesBack()
    {
        var session = TestWorld.NewGame();
        session.ToDay(6);

        Assert.True(session.Views.Morning().Returns.Single().CanNoteReturn);
        session.Do(new NoteReturn("loan-1"));

        var row = Assert.Single(session.Views.Notebook("board-clerk").Ledger);
        Assert.Equal(("3 Seedmonth", "8 Seedmonth"), (row.BorrowedDate, row.ReturnedDate));
        Assert.True(session.Views.Morning().Returns.Single().Noted);
    }

    [Fact]
    public void TheReturnMomentPassesWithTheDay()
    {
        var session = TestWorld.NewGame();
        session.ToDay(7);

        session.Refused(new NoteReturn("loan-1"), RejectionReason.NotTheMoment);
    }

    [Fact]
    public void AFragmentCanBeCopiedIntoTheNotebookOnce()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.FinishDayUntil("wren-hale-1");
        session.Refused(new NoteThought(), RejectionReason.NothingToNote);
        session.Do(new ReadThoughts());

        session.Do(new NoteThought());

        var thought = Assert.Single(session.Views.Notebook("wren-hale").Thoughts);
        Assert.Equal(("4 Seedmonth", "Twelve paces of bare earth. Twelve at least."), (thought.Date, thought.Fragment));
        session.Refused(new NoteThought(), RejectionReason.AlreadyNoted);
    }

    [Fact]
    public void TheNotebookHeaderReadsTrustAndVisits()
    {
        var session = TestWorld.NewGame();
        session.ToDay(4);

        var page = session.Views.Notebook("elara-voss");
        Assert.Equal(1, page.VisitCount);
        Assert.Equal(1, page.Trust);
        Assert.Equal("4 Seedmonth", page.FirstVisitDate);
        Assert.Equal(["board-clerk", "elara-voss", "ada-marsh", "wren-hale", "nell-hart", "osric-penn"], session.Views.NotebookIndex().Select(e => e.PatronId));
    }

    [Fact]
    public void FreeTextIsSavedBesideTheLog()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");
        session.Do(new LendRequested());
        session.Do(new NoteLoan("loan-1"));
        session.Notes.SetFreeNotes("board-clerk", "Grey gloves. Never took them off.");
        session.Notes.SetLedgerNote("loan-1", "Wanted it today.");

        var restored = GameSession.Restore(session.Content, SaveSerializer.Deserialize(SaveSerializer.Serialize(session.ToSave())));

        var page = restored.Views.Notebook("board-clerk");
        Assert.Equal("Grey gloves. Never took them off.", page.FreeNotes);
        Assert.Equal("Wanted it today.", page.Ledger.Single().Note);
    }

    [Fact]
    public void TheCardShowsTheTrueHistory()
    {
        var session = TestWorld.NewGame();

        var card = session.Views.Card("ada-marsh");

        Assert.Equal(4, card.Loans.Count);
        Assert.Equal("22 Emberfall", card.FirstVisitDate);
        Assert.All(card.Loans, l => Assert.Equal("Hearth & Kettle", l.BookTitle));
        Assert.Equal(("27 Frostwane", "1 Seedmonth"), (card.Loans[^1].BorrowedDate, card.Loans[^1].ReturnedDate));
    }
}
