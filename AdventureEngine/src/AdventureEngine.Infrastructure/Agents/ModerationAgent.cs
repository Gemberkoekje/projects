namespace AdventureEngine.Infrastructure.Agents;

using AdventureEngine.Infrastructure.Anthropic;

/// <summary>
/// Lightweight Haiku-based moderation check for player-supplied premises.
/// </summary>
internal sealed class ModerationAgent
{
    private readonly AnthropicClient _client;
    private readonly ILogger<ModerationAgent> _logger;

    public ModerationAgent(IOptions<AnthropicOptions> options, ILogger<ModerationAgent> logger)
    {
        _client = new AnthropicClient(new APIAuthentication(options.Value.ApiKey));
        _logger = logger;
    }

    /// <summary>
    /// Returns (result, usage): result indicates whether the premise is safe and includes an optional reason.
    /// </summary>
    public async Task<(ModerationResult Result, AgentUsage Usage)> IsSafeAsync(string premise, CancellationToken ct = default)
    {
        var parameters = new MessageParameters
        {
            Messages = new List<Message>
            {
                new Message(RoleType.User,
                    $$"""
                    Evaluate whether this game premise is appropriate for an interactive fiction adventure.
                    Reject premises that contain: graphic sexual content, instructions for real-world violence/harm,
                    hate speech targeting real groups, or content involving minors in harmful situations.
                    Permitted: fantasy violence, mature themes, morally complex stories.

                    Premise: {{premise}}

                    Reply with strict JSON only:
                    {"result":"SAFE"}
                    or
                    {"result":"UNSAFE","reason":"<short reason>"}
                    """),
            },
            System = new List<SystemMessage>
            {
                new SystemMessage("You are a content moderation assistant. Output strict JSON only with result SAFE/UNSAFE and an UNSAFE reason."),
            },
            MaxTokens = 128,
            Model = AnthropicModels.Claude45Haiku,
            Stream = false,
            Temperature = 0m,
        };

        var response = await _client.Messages.GetClaudeMessageAsync(parameters, ct);
        var verdict = response.Message.ToString().Trim();
        var ru = response.Usage;
        var usage = new AgentUsage(ru.InputTokens, ru.OutputTokens, ru.CacheReadInputTokens, ru.CacheCreationInputTokens);

        if (!ModerationResultParser.TryParse(verdict, out var result))
        {
            _logger.LogWarning("Moderation agent returned an unparseable response; defaulting to SAFE. Response: {Response}", verdict);
            return (ModerationResult.Safe, usage);
        }

        _logger.LogDebug("Moderation verdict for premise: {Verdict} ({Reason})", result.IsSafe ? "SAFE" : "UNSAFE", result.Reason ?? "no reason");
        return (result, usage);
    }
}
