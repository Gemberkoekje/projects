using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Events;

namespace Curator.Core.Tests;

public sealed class DeskTests
{
    [Fact]
    public void MissingDefaultRefusalIsAnError()
    {
        var source = TestWorld.Source();
        source.Edit("desk.json", n => n["refusals"]!.AsObject().Remove(DeskFile.DefaultRefusal));

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.File == "desk.json" && p.JsonPath == "$.refusals");
    }

    [Fact]
    public void RefusalKeysMustBeRefusalReasons()
    {
        var source = TestWorld.Source();
        source.Edit("desk.json", n => n["refusals"]!["patronAtTheCounter"] = "Someone's there.");

        var problem = Assert.Single(source.Check(), p => p.Severity == ProblemSeverity.Error);
        Assert.Equal("$.refusals.patronAtTheCounter", problem.JsonPath);
    }

    [Fact]
    public void EmptyDeskLinesAreErrors()
    {
        var source = TestWorld.Source();
        source.Edit("desk.json", n =>
        {
            n["pageResisted"] = " ";
            n["refusals"]!["noVisit"] = "";
        });

        var problems = source.Check().Where(p => p.Severity == ProblemSeverity.Error).Select(p => p.JsonPath).ToList();
        Assert.Contains("$.pageResisted", problems);
        Assert.Contains("$.refusals.noVisit", problems);
    }

    [Fact]
    public void ARefusalGetsItsOwnLineOrTheDefault()
    {
        var session = TestWorld.NewGame();
        var refusals = TestWorld.Content.Desk.Refusals;

        Assert.Equal(refusals["patronAtCounter"], session.Views.RefusalLine(RejectionReason.PatronAtCounter));
        Assert.False(refusals.ContainsKey("unknownLoan"));
        Assert.Equal(refusals[DeskFile.DefaultRefusal], session.Views.RefusalLine(RejectionReason.UnknownLoan));
    }

    [Fact]
    public void TheIdentifyNoteSaysWhatIdentifyTurnedUp()
    {
        var session = TestWorld.NewGame();
        var desk = TestWorld.Content.Desk;

        Assert.Equal(desk.IdentifyFailed, session.Views.IdentifyNote([new ManaSpent(1, 0, "identify"), new IdentifyFailed("lanterns")]));
        Assert.Equal(desk.PageResisted, session.Views.IdentifyNote([new PageResisted("lanterns", "lanterns-1")]));
        Assert.Equal(desk.PageRepeated, session.Views.IdentifyNote([new PageIdentified("lanterns", "lanterns-1", Repeat: true)]));
        Assert.Equal("", session.Views.IdentifyNote([new PageIdentified("lanterns", "lanterns-1", Repeat: false)]));
    }

    [Fact]
    public void TheViewsCarryTheDeskNotes()
    {
        var session = TestWorld.NewGame();
        var desk = TestWorld.Content.Desk;

        var book = session.Views.Book("lanterns");
        Assert.Equal(desk.PageResisted, book.ResistedNote);
        Assert.Equal(desk.PageRemoved, book.RemovedNote);
        Assert.Equal(desk.CloseEarly, session.Views.Hud().CloseEarlyPrompt);
    }
}
