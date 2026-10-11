using Curator.Core.Commands;
using Curator.Core.Events;
using Curator.Core.Projections;

namespace Curator.Core.Tests;

public sealed class TranscriptTests
{
    [Fact]
    public void TheTranscriptKeepsTheVisitInOrder()
    {
        var session = PatienceAndTrustTests.Elara();
        var question = session.Views.Visit().Questions.First(q => !q.Specific);

        session.Do(new AskQuestion(question.Id));
        session.Do(new OfferBook("lanterns"));
        session.Do(new OfferBook("hearth-and-kettle"));

        var visit = session.Views.Visit();
        Assert.Equal(
            [SpeechKind.Greeting, SpeechKind.Request, SpeechKind.Asked, SpeechKind.Answer, SpeechKind.Offered, SpeechKind.Refused, SpeechKind.Offered, SpeechKind.Exit],
            visit.Transcript.Select(l => l.Kind));
        Assert.Equal(visit.Greeting, visit.Transcript[0].Text);
        Assert.Equal(question.Text, visit.Transcript[2].Text);
        Assert.Equal("Lanterns and Lesser Fires", visit.Transcript[4].Text);
        Assert.Equal("That's not quite what I meant.", visit.Transcript[5].Text);
        Assert.Equal(visit.ExitLine, visit.Transcript[^1].Text);
    }

    [Fact]
    public void LendingTheRequestedBookIsNotAnOffer()
    {
        var session = PatienceAndTrustTests.Elara();

        session.Do(new LendRequested());

        Assert.Equal([SpeechKind.Greeting, SpeechKind.Request, SpeechKind.Exit], session.Views.Visit().Transcript.Select(l => l.Kind));
    }

    [Fact]
    public void AnEmptyCounterHasNoTranscript()
    {
        var session = TestWorld.NewGame();

        Assert.Empty(session.Views.Visit().Transcript);
    }

    [Fact]
    public void AGenericWalkOutLineIsNarrationNotSpeech()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/elara-voss.json", n => n["visits"]![0]!["decisions"]!.AsObject().Remove("walkedOut"));
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(2);
        session.Ring("elara-voss-1");
        foreach (var question in new[] { "purpose", "knowledge", "source", "experience" })
        {
            session.Do(new AskQuestion(question));
        }

        session.Do(new Identify("household-arts-3"));
        session.Do(new Identify("household-arts-3"));

        Assert.Single(session.All<PatronWalkedOut>());
        var last = session.Views.Visit().Transcript[^1];
        Assert.Equal((SpeechKind.Narration, TestWorld.Content.Questions.GenericLines.WalkedOut), (last.Kind, last.Text));
    }

    [Fact]
    public void APatronsOwnWalkOutLineIsSpeech()
    {
        var session = PatienceAndTrustTests.Elara();
        foreach (var question in new[] { "purpose", "knowledge", "source", "experience" })
        {
            session.Do(new AskQuestion(question));
        }

        session.Do(new Identify("household-arts-3"));
        session.Do(new Identify("household-arts-3"));

        Assert.Equal(SpeechKind.Exit, session.Views.Visit().Transcript[^1].Kind);
    }
}
