namespace AdventureEngine.Infrastructure.Agents;

using AdventureEngine.Infrastructure.Anthropic;

/// <summary>
/// NPC agent using claude-haiku-4-5 for in-character responses.
/// </summary>
internal sealed class NpcAgent : INpcAgent
{
    private readonly AnthropicClient _client;

    public NpcAgent(IOptions<AnthropicOptions> options)
    {
        _client = new AnthropicClient(new APIAuthentication(options.Value.ApiKey));
    }

    public async Task<(string Dialogue, AgentUsage Usage)> RespondAsync(NpcContext ctx, CancellationToken ct = default)
    {
        var systemPrompt = $"""
            You are {ctx.Npc.Name}. Respond only as this character. Stay in voice.
            Keep responses to 1-3 sentences.
            """;

        var characterSheet = $"""
            Personality: {ctx.Npc.Personality}
            Backstory: {ctx.Npc.Backstory}
            Relationship to player: {ctx.Npc.RelationshipToPlayer}
            Speech style: {ctx.Npc.SpeechStyle}
            Secrets (never reveal directly): {ctx.Npc.Secrets}
            """;

        var sceneContext = $"""
            Scene: {ctx.SceneDescription}
            Recent events: {ctx.RecentNarratorText}
            Player action/words: {ctx.PlayerInput}
            """;

        var parameters = new MessageParameters
        {
            Messages = new List<Message>
            {
                new Message(RoleType.User, $"{characterSheet}\n\n{sceneContext}"),
            },
            System = new List<SystemMessage>
            {
                new SystemMessage(systemPrompt),
            },
            MaxTokens = 150,
            Model = AnthropicModels.Claude45Haiku,
            Stream = false,
            Temperature = 1.0m,
        };

        var response = await _client.Messages.GetClaudeMessageAsync(parameters, ct);
        var usage = response.Usage;
        return (response.Message.ToString(), new AgentUsage(
            usage.InputTokens,
            usage.OutputTokens,
            usage.CacheReadInputTokens,
            usage.CacheCreationInputTokens));
    }
}
