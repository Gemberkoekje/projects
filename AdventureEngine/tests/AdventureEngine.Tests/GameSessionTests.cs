using AdventureEngine.Domain;
using AdventureEngine.Domain.Events;
using System.Reflection;
using System.Text.Json;

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

        session.Apply(new UsageRecorded(session.Id, "narrator", 1000, 300, DateTime.UtcNow, 200, 50));
        session.Apply(new UsageRecorded(session.Id, "npc", 800, 100, DateTime.UtcNow, 100, 25));

        Assert.Equal(1800, session.TotalInputTokens);
        Assert.Equal(400, session.TotalOutputTokens);
        Assert.Equal(300, session.TotalCacheReadInputTokens);
        Assert.Equal(75, session.TotalCacheCreationInputTokens);
    }

    [Fact]
    public void Apply_UsageRecorded_AccumulatesAllNonStreamingAgentTags()
    {
        var session = new GameSession();
        session.Apply(MakeSessionCreated());

        // Simulate events emitted during session creation
        session.Apply(new UsageRecorded(session.Id, "moderation", 50, 5, DateTime.UtcNow));
        session.Apply(new UsageRecorded(session.Id, "director", 3000, 800, DateTime.UtcNow, 0, 100));

        // Simulate events emitted during a player action
        session.Apply(new UsageRecorded(session.Id, "npc", 200, 30, DateTime.UtcNow));

        // Simulate events emitted during a chapter boundary
        session.Apply(new UsageRecorded(session.Id, "chapter_summary", 500, 120, DateTime.UtcNow));

        Assert.Equal(3750, session.TotalInputTokens);
        Assert.Equal(955, session.TotalOutputTokens);
        Assert.Equal(0, session.TotalCacheReadInputTokens);
        Assert.Equal(100, session.TotalCacheCreationInputTokens);
    }

    [Fact]
    public void UsageRecorded_DeserializesLegacyPayloadWithZeroCacheDefaults()
    {
        var occurredAt = DateTime.UtcNow;
        var json = $$"""
                     {
                       "SessionId": "{{Guid.NewGuid()}}",
                       "Agent": "director",
                       "InputTokens": 120,
                       "OutputTokens": 45,
                       "OccurredAt": "{{occurredAt:O}}"
                     }
                     """;

        var evt = JsonSerializer.Deserialize<UsageRecorded>(json);

        Assert.NotNull(evt);
        Assert.Equal(0, evt!.CacheReadInputTokens);
        Assert.Equal(0, evt.CacheCreationInputTokens);
    }

    [Fact]
    public void ParseStateMarkers_StripsLeakedDisplayArtifacts()
    {
        const string raw = "Narrative begins.\n[New scene: Internal note]\n_streamBuffer▌\nNarrative continues.";

        var (response, nextSceneId, outcomeWon) = InvokeParseStateMarkers(raw);

        Assert.Equal("Narrative begins.\n\nNarrative continues.", response);
        Assert.Null(nextSceneId);
        Assert.Null(outcomeWon);
    }

    [Fact]
    public void ParseStateMarkers_ParsesSceneAndOutcomeMarkers()
    {
        const string raw = "You move forward. <!-- SCENE:ch2_sc1 --> <!-- OUTCOME:WON -->";

        var (response, nextSceneId, outcomeWon) = InvokeParseStateMarkers(raw);

        Assert.Equal("You move forward.", response);
        Assert.Equal("ch2_sc1", nextSceneId);
        Assert.True(outcomeWon);
    }

    private static (string response, string? nextSceneId, bool? outcomeWon) InvokeParseStateMarkers(string raw)
    {
        var infrastructureAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "AdventureEngine.Infrastructure")
            ?? Assembly.Load("AdventureEngine.Infrastructure");

        var serviceType = infrastructureAssembly.GetType("AdventureEngine.Infrastructure.GameSessionService", throwOnError: true)
            ?? throw new InvalidOperationException("GameSessionService type not found.");

        var parseMethod = serviceType.GetMethod("ParseStateMarkers", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ParseStateMarkers method not found.");

        var result = parseMethod.Invoke(null, new object[] { raw })
            ?? throw new InvalidOperationException("ParseStateMarkers returned null.");

        return ((string, string?, bool?))result;
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
