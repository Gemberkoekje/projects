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
    /// Returns true if the premise is safe to use as a game prompt, false if it should be rejected.
    /// </summary>
    public async Task<bool> IsSafeAsync(string premise, CancellationToken ct = default)
    {
        var parameters = new MessageParameters
        {
            Messages = new List<Message>
            {
                new Message(RoleType.User,
                    $"""
                    Evaluate whether this game premise is appropriate for an interactive fiction adventure.
                    Reject premises that contain: graphic sexual content, instructions for real-world violence/harm,
                    hate speech targeting real groups, or content involving minors in harmful situations.
                    Permitted: fantasy violence, mature themes, morally complex stories.

                    Premise: {premise}

                    Reply with exactly one word: SAFE or UNSAFE
                    """),
            },
            System = new List<SystemMessage>
            {
                new SystemMessage("You are a content moderation assistant. Reply with exactly one word: SAFE or UNSAFE."),
            },
            MaxTokens = 10,
            Model = AnthropicModels.Claude45Haiku,
            Stream = false,
            Temperature = 0m,
        };

        var response = await _client.Messages.GetClaudeMessageAsync(parameters, ct);
        var verdict = response.Message.ToString().Trim();

        if (string.IsNullOrEmpty(verdict))
        {
            _logger.LogWarning("Moderation agent returned an empty response; defaulting to SAFE");
            return true;
        }

        _logger.LogDebug("Moderation verdict for premise: {Verdict}", verdict);
        return !verdict.StartsWith("UNSAFE", StringComparison.OrdinalIgnoreCase);
    }
}
