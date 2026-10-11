using Curator.Core.Commands;
using Curator.Core.Events;
using Curator.Core.Game;

namespace Curator.Core.Tests;

public sealed class QuestionTests
{
    [Fact]
    public void StandardQuestionsAreAnsweredAtTheCurrentTrust()
    {
        var session = PatienceAndTrustTests.Elara();

        session.Do(new AskQuestion("purpose"));

        Assert.Equal("Mildew, mostly. On old cloth. It's delicate work.", session.Last<QuestionAsked>().Answer);
        Assert.Equal("Mildew, mostly. On old cloth. It's delicate work.", session.Views.Visit().LastLine);
    }

    [Fact]
    public void AnswersFallBackToTheNearestLowerTrust()
    {
        var session = ElaraAtTrust(3);

        session.Do(new AskQuestion("purpose"));

        Assert.Equal("Old cloth bindings. They're spotting faster than I can clean them.", session.Last<QuestionAsked>().Answer);
    }

    [Fact]
    public void WithNoAnswerAtOrBelowTrustThePatronDeflects()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.FinishDayUntil("wren-hale-1");

        session.Do(new AskQuestion("knowledge"));

        Assert.Contains(session.Last<QuestionAsked>().Answer, TestWorld.Content.Questions.Deflections[0]);
    }

    [Fact]
    public void EachQuestionIsAskedOncePerVisit()
    {
        var session = PatienceAndTrustTests.Elara();
        session.Do(new AskQuestion("purpose"));

        session.Refused(new AskQuestion("purpose"), RejectionReason.QuestionAlreadyAsked);
        Assert.True(session.Views.Visit().Questions.Single(q => q.Id == "purpose").Asked);
    }

    [Fact]
    public void LockedQuestionsCantBeAsked()
    {
        var session = PatienceAndTrustTests.Elara();

        session.Refused(new AskQuestion("elara-voss-1-newt"), RejectionReason.QuestionUnavailable);
        session.Refused(new AskQuestion("no-such-question"), RejectionReason.QuestionUnavailable);
    }

    [Fact]
    public void TopicRequestsUseTheTopicWording()
    {
        var session = TestWorld.NewGame();
        session.ToDay(3);
        session.FinishDayUntil("osric-penn-1");

        var knowledge = session.Views.Visit().Questions.Single(q => q.Id == "knowledge");
        Assert.Equal("Do you know which spell you're after?", knowledge.Text);
    }

    [Fact]
    public void ReadThoughtsSurfacesAFragmentAndUnlocksItsQuestion()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.FinishDayUntil("wren-hale-1");

        session.Do(new ReadThoughts());

        var visit = session.Views.Visit();
        Assert.Equal("Twelve paces of bare earth. Twelve at least.", visit.Fragment);
        var unlocked = visit.Questions.Single(q => q.Id == "wren-hale-1-paces");
        Assert.True(unlocked.Specific);
        session.Do(new AskQuestion("wren-hale-1-paces"));
        Assert.Equal("Nothing you'd know.", session.Last<QuestionAsked>().Answer);
        Assert.Contains("wren-hale:asked-paces", session.State.Flags);
    }

    [Fact]
    public void FragmentsCanDependOnTrust()
    {
        var session = ElaraAtTrust(2);

        session.Do(new ReadThoughts());

        Assert.Equal("Eye of newt, beeswax, and then it keeps. It finally keeps.", session.Views.Visit().Fragment);
    }

    [Fact]
    public void ReadThoughtsWorksOncePerVisit()
    {
        var session = PatienceAndTrustTests.Elara();
        session.Do(new ReadThoughts());

        session.Refused(new ReadThoughts(), RejectionReason.AlreadyReadThoughts);
    }

    [Fact]
    public void AWardedMindNoticesReadThoughts()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/lysander-quill.json", n => n["startTrust"] = 3);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(4);
        session.FinishDayUntil("lysander-quill-1");

        session.Do(new ReadThoughts());

        var visit = session.Views.Visit();
        Assert.True(visit.Noticed);
        Assert.Equal("", visit.Fragment);
        Assert.Equal(1, session.State.Patron("lysander-quill").Trust);
        Assert.Contains("lysander-quill:noticed", session.State.Flags);
    }

    [Fact]
    public void IdentifiedPagesUnlockQuestions()
    {
        var session = TestWorld.NewDebugGame();
        session.ToDay(2);
        session.FinishDayUntil("wren-hale-1");
        Assert.DoesNotContain(session.Views.Visit().Questions, q => q.Id == "wren-hale-1-fireball");

        session.Do(new DebugRevealBook("lanterns"));

        Assert.Contains(session.Views.Visit().Questions, q => q.Id == "wren-hale-1-fireball");
    }

    [Fact]
    public void CardHistoryAndFlagsUnlockQuestions()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/ada-marsh.json", n => n["visits"]![0]!["questions"] = System.Text.Json.Nodes.JsonNode.Parse("""
            [
              { "id": "ada-marsh-1-usual", "text": "The usual again?", "unlockedBy": "cardBook:hearth-and-kettle", "answers": { "0": "Always." } },
              { "id": "ada-marsh-1-clerk", "text": "Did you see the clerk?", "unlockedBy": "flag:board-clerk:took-common-wards", "answers": { "0": "Grey gloves." } },
              { "id": "ada-marsh-1-never", "text": "Never shown?", "unlockedBy": "flag:ada-marsh:never", "answers": { "0": "No." } }
            ]
            """));
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(2);
        session.FinishDayUntil("ada-marsh-1");

        var questions = session.Views.Visit().Questions.Select(q => q.Id).ToList();
        Assert.Contains("ada-marsh-1-usual", questions);
        Assert.Contains("ada-marsh-1-clerk", questions);
        Assert.DoesNotContain("ada-marsh-1-never", questions);
    }

    private static GameSession ElaraAtTrust(int trust)
    {
        var source = TestWorld.Source();
        source.Edit("patrons/elara-voss.json", n => n["startTrust"] = trust);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(2);
        session.Ring("elara-voss-1");
        return session;
    }
}
