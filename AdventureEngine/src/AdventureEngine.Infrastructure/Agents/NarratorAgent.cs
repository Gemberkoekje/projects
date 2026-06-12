namespace AdventureEngine.Infrastructure.Agents;

using AdventureEngine.Infrastructure.Anthropic;

/// <summary>
/// Narrator agent using claude-sonnet-4-6 with streaming and prompt caching.
/// </summary>
internal sealed class NarratorAgent : INarratorAgent
{
    private const int MaxExitDescriptionLength = 60;

    private static readonly string NarratorSystemPrompt =
        """
        You are the narrator of an interactive fiction adventure.
        Be vivid, second-person, present tense. Respond to player actions.
        Keep responses to 150-250 words unless drama demands more.
        Honour established facts in the story context below.

        After your narrative response, if any of these apply, add them on their own new lines at the very end:
        - If the player moves to a new scene, add: <!-- SCENE:{scene_id} --> (use the exact exit scene ID listed below)
        - If the win condition is clearly met, add: <!-- OUTCOME:WON -->
        - If the lose condition is clearly met, add: <!-- OUTCOME:LOST -->
        Never invent scene IDs. Only emit a SCENE tag when the player is clearly navigating to a listed exit.
        """;

    private readonly AnthropicClient _client;

    public NarratorAgent(IOptions<AnthropicOptions> options)
    {
        _client = new AnthropicClient(new APIAuthentication(options.Value.ApiKey));
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        NarratorContext ctx,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chapter = ctx.WorldManifest.Chapters.FirstOrDefault(c => c.Index == ctx.CurrentChapterIndex);
        var scene = chapter?.Scenes.FirstOrDefault(s => s.Id == ctx.CurrentSceneId);
        var activeNpcs = scene is not null
            ? ctx.WorldManifest.Npcs.Where(n => scene.ActiveNpcIds.Contains(n.Id)).ToList()
            : new List<NpcDefinition>();

        var worldAnchor = BuildWorldAnchor(ctx.WorldManifest);
        var storyProgress = BuildStoryProgress(ctx.ChapterSummaries);
        var currentChapter = BuildCurrentChapter(chapter, scene, activeNpcs, ctx.WorldManifest);
        var recentHistory = BuildRecentHistory(ctx.RecentHistory);

        var userContent = $"""
            {worldAnchor}

            {storyProgress}

            {currentChapter}

            {recentHistory}

            Player action: {ctx.PlayerInput}
            """;

        var parameters = new MessageParameters
        {
            Messages = new List<Message>
            {
                new Message(RoleType.User, userContent),
            },
            System = new List<SystemMessage>
            {
                new SystemMessage(NarratorSystemPrompt),
            },
            MaxTokens = 512,
            Model = AnthropicModels.Claude46Sonnet,
            Stream = true,
            Temperature = 1.0m,
            PromptCaching = PromptCacheType.AutomaticToolsAndSystem,
        };

        await foreach (var res in _client.Messages.StreamClaudeMessageAsync(parameters, ct))
        {
            if (res.Delta?.Text is { Length: > 0 } text)
                yield return text;
        }
    }

    public async Task<string> GenerateChapterSummaryAsync(
        WorldManifest world,
        int chapterIndex,
        IReadOnlyList<(string PlayerInput, string NarratorResponse)> chapterHistory,
        CancellationToken ct = default)
    {
        var historyText = string.Join("\n\n", chapterHistory.Select((h, i) =>
            $"Player: {h.PlayerInput}\nNarrator: {h.NarratorResponse}"));

        var chapter = world.Chapters.FirstOrDefault(c => c.Index == chapterIndex);

        var parameters = new MessageParameters
        {
            Messages = new List<Message>
            {
                new Message(RoleType.User,
                    $"""
                    Chapter {chapterIndex}: {chapter?.Title ?? "Unknown"}

                    {historyText}

                    Summarise what happened in chapter {chapterIndex} in 80-100 words.
                    Focus on: key decisions, NPC relationship changes, items gained/lost, mysteries opened or closed.
                    """),
            },
            System = new List<SystemMessage>
            {
                new SystemMessage(NarratorSystemPrompt),
            },
            MaxTokens = 200,
            Model = AnthropicModels.Claude46Sonnet,
            Stream = false,
            Temperature = 0.5m,
        };

        var response = await _client.Messages.GetClaudeMessageAsync(parameters, ct);
        return response.Message.ToString();
    }

    private static string BuildWorldAnchor(WorldManifest world)
    {
        var npcList = string.Join("\n", world.Npcs.Select(n => $"- {n.Name}: {n.Personality}"));
        return $"""
            [WORLD: {world.Title}]
            Premise: {world.Premise}
            Win condition: {world.WinCondition}
            Lose condition: {world.LoseCondition}
            NPCs: {npcList}
            """;
    }

    private static string BuildStoryProgress(IReadOnlyDictionary<int, string> summaries)
    {
        if (summaries.Count == 0)
            return "[STORY SO FAR]\nThis is the beginning of the adventure.";

        var entries = summaries
            .OrderBy(kv => kv.Key)
            .Select(kv => $"Chapter {kv.Key}: {kv.Value}");

        return $"[STORY SO FAR]\n{string.Join("\n\n", entries)}";
    }

    private static string BuildCurrentChapter(
        ChapterDefinition? chapter,
        SceneDefinition? scene,
        List<NpcDefinition> npcs,
        WorldManifest world)
    {
        if (chapter is null || scene is null)
            return "[CURRENT SCENE]\nUnknown location.";

        var npcNames = npcs.Count > 0
            ? string.Join(", ", npcs.Select(n => n.Name))
            : "none";

        var allScenes = world.Chapters.SelectMany(c => c.Scenes).ToDictionary(s => s.Id);
        var exits = scene.Exits.Count > 0
            ? string.Join("\n", scene.Exits.Select(id =>
                allScenes.TryGetValue(id, out var exitScene)
                    ? $"  {id}: {exitScene.Description[..Math.Min(MaxExitDescriptionLength, exitScene.Description.Length)].TrimEnd()}…"
                    : $"  {id}"))
            : "  none visible";

        return $"""
            [CURRENT SCENE — Chapter {chapter.Index}: {chapter.Title}]
            {scene.Description}
            Active NPCs: {npcNames}
            Available exits:
            {exits}
            """;
    }

    private static string BuildRecentHistory(IReadOnlyList<(string PlayerInput, string NarratorResponse)> history)
    {
        if (history.Count == 0)
            return "[RECENT HISTORY]\nNone yet.";

        var entries = history.Select(h => $"Player: {h.PlayerInput}\nNarrator: {h.NarratorResponse}");
        return $"[RECENT HISTORY]\n{string.Join("\n\n", entries)}";
    }
}
