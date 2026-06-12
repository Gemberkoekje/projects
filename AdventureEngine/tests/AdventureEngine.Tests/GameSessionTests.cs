using AdventureEngine.Domain;
using AdventureEngine.Domain.Events;

namespace AdventureEngine.Tests;

public class GameSessionTests
{
    [Fact]
    public void Apply_PlayerActed_AddsToRecentHistoryAndChapterHistory()
    {
        var session = new GameSession();
        session.Apply(MakeSessionCreated());

        session.Apply(new PlayerActed(session.Id, "go north", "You head north.", DateTime.UtcNow));

        Assert.Single(session.RecentHistory);
        Assert.Single(session.CurrentChapterHistory);
        Assert.Equal("go north", session.RecentHistory[0].PlayerInput);
        Assert.Equal("go north", session.CurrentChapterHistory[0].PlayerInput);
    }

    [Fact]
    public void Apply_PlayerActed_SlidesWindowAfterSix()
    {
        var session = new GameSession();
        session.Apply(MakeSessionCreated());

        for (int i = 1; i <= 7; i++)
            session.Apply(new PlayerActed(session.Id, $"action {i}", $"response {i}", DateTime.UtcNow));

        Assert.Equal(6, session.RecentHistory.Count);
        Assert.Equal("action 2", session.RecentHistory[0].PlayerInput);
        Assert.Equal(7, session.CurrentChapterHistory.Count);
    }

    [Fact]
    public void Apply_ChapterCompleted_AdvancesChapterAndClearsHistory()
    {
        var session = new GameSession();
        session.Apply(MakeSessionCreated());
        session.Apply(new PlayerActed(session.Id, "look", "You see things.", DateTime.UtcNow));

        session.Apply(new ChapterCompleted(session.Id, 1, "Chapter 1 summary.", DateTime.UtcNow));

        Assert.Equal(2, session.CurrentChapterIndex);
        Assert.Equal("Chapter 1 summary.", session.ChapterSummaries[1]);
        Assert.Empty(session.CurrentChapterHistory);
        Assert.Equal("ch2_sc1", session.CurrentSceneId);
    }

    [Fact]
    public void Apply_SceneEntered_UpdatesCurrentScene()
    {
        var session = new GameSession();
        session.Apply(MakeSessionCreated());

        session.Apply(new SceneEntered(session.Id, "ch1_sc2", string.Empty, DateTime.UtcNow));

        Assert.Equal("ch1_sc2", session.CurrentSceneId);
    }

    [Fact]
    public void Apply_GameEnded_Won_SetsStatusCompleted()
    {
        var session = new GameSession();
        session.Apply(MakeSessionCreated());

        session.Apply(new GameEnded(session.Id, true, "You won!", DateTime.UtcNow));

        Assert.Equal(SessionStatus.Completed, session.Status);
    }

    [Fact]
    public void Apply_GameEnded_Lost_SetsStatusAbandoned()
    {
        var session = new GameSession();
        session.Apply(MakeSessionCreated());

        session.Apply(new GameEnded(session.Id, false, "You lost.", DateTime.UtcNow));

        Assert.Equal(SessionStatus.Abandoned, session.Status);
    }

    [Fact]
    public void Apply_UsageRecorded_AccumulatesTokens()
    {
        var session = new GameSession();
        session.Apply(MakeSessionCreated());

        session.Apply(new UsageRecorded(session.Id, "narrator", 1000, 300, DateTime.UtcNow));
        session.Apply(new UsageRecorded(session.Id, "npc", 800, 100, DateTime.UtcNow));

        Assert.Equal(1800, session.TotalInputTokens);
        Assert.Equal(400, session.TotalOutputTokens);
    }

    private static SessionCreated MakeSessionCreated()
    {
        var world = new WorldManifest
        {
            Title = "Test World",
            Premise = "A test.",
            WinCondition = "Win.",
            LoseCondition = "Lose.",
            Chapters = new[]
            {
                new ChapterDefinition
                {
                    Index = 1, Title = "Chapter One", Summary = "First.",
                    Scenes = new[]
                    {
                        new SceneDefinition { Id = "ch1_sc1", Description = "First scene.", Exits = new[] { "ch1_sc2" } },
                        new SceneDefinition { Id = "ch1_sc2", Description = "Second scene.", Exits = new[] { "ch2_sc1" } },
                    }
                },
                new ChapterDefinition
                {
                    Index = 2, Title = "Chapter Two", Summary = "Second.",
                    Scenes = new[]
                    {
                        new SceneDefinition { Id = "ch2_sc1", Description = "Chapter two start.", Exits = Array.Empty<string>() },
                    }
                },
            },
            Npcs = Array.Empty<NpcDefinition>(),
        };

        return new SessionCreated(Guid.NewGuid(), "testplayer", "test prompt", world, DateTime.UtcNow);
    }
}
