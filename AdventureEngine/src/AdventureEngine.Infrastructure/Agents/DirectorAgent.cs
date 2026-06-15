namespace AdventureEngine.Infrastructure.Agents;

using System.Text.Json;
using AdventureEngine.Application.Agents;
using AdventureEngine.Infrastructure.Anthropic;

/// <summary>
/// One-shot Director agent that generates a WorldManifest using claude-opus-4-6.
/// </summary>
internal sealed class DirectorAgent : IDirectorAgent
{
    private static readonly string WorldManifestSchema = """
        {
          "title": "string",
          "premise": "string",
          "lore": "string",
          "win_condition": "string",
          "lose_condition": "string",
          "chapters": [
            {
              "index": 1,
              "title": "string",
              "summary": "string",
              "scenes": [
                {
                  "id": "ch1_sc1",
                  "description": "string",
                  "active_npc_ids": ["npc_id"],
                  "exits": ["ch1_sc2"]
                }
              ]
            }
          ],
          "npcs": [
            {
              "id": "npc_id",
              "name": "string",
              "personality": "string",
              "backstory": "string",
              "relationship_to_player": "string",
              "secrets": "string",
              "speech_style": "string"
            }
          ]
        }
        """;

    private readonly AnthropicClient _client;
    private readonly ILogger<DirectorAgent> _logger;

    public DirectorAgent(IOptions<AnthropicOptions> options, ILogger<DirectorAgent> logger)
    {
        _client = new AnthropicClient(new APIAuthentication(options.Value.ApiKey));
        _logger = logger;
    }

    public async Task<(WorldManifest World, AgentUsage Usage)> GenerateWorldAsync(string playerPrompt, CancellationToken ct = default)
    {
        _logger.LogInformation("Director generating world for prompt: {Prompt}", playerPrompt);

        var systemPrompt = $$"""
            You are a creative director generating a 5-chapter interactive fiction adventure.
            Generate ONLY valid JSON matching the WorldManifest schema below.
            Do not include any text outside the JSON object.
            Keep the output concise so it fits comfortably in a single response: use short titles, brief chapter summaries, and scene descriptions of roughly 2-4 sentences each.

            Schema:
            {{WorldManifestSchema}}

            Requirements:
            - 5 chapters, 2-4 scenes each
            - 3-5 named NPCs with distinct personalities and secrets
            - A clear win condition and a meaningful lose condition
            - Internal consistency: NPCs referenced in scenes must exist in the npcs array
            - Scene IDs must follow pattern ch{n}_sc{n} (e.g. ch1_sc1)
            - NPC IDs must be unique snake_case identifiers (e.g. npc_ryo)
            """;

        var parameters = new MessageParameters
        {
            Messages = new List<Message>
            {
                new Message(RoleType.User, $"Player premise: {playerPrompt}"),
            },
            System = new List<SystemMessage>
            {
                new SystemMessage(systemPrompt),
            },
            MaxTokens = 8192,
            Model = AnthropicModels.Claude46Opus,
            Stream = false,
            Temperature = 1.0m,
        };

        var response = await _client.Messages.GetClaudeMessageAsync(parameters, ct);
        var rawResponse = response.Message.ToString();
        var json = JsonResponseParser.ExtractJsonPayload(rawResponse);

        WorldManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<WorldManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Director returned invalid WorldManifest JSON.\n\nRaw response:\n{rawResponse}\n\nExtracted JSON:\n{json}",
                ex);
        }

        if (manifest is null)
            throw new InvalidOperationException("Director returned null WorldManifest.");

        var usage = response.Usage;
        _logger.LogInformation("Director generated world: {Title}", manifest.Title);
        return (manifest, new AgentUsage(
            usage.InputTokens,
            usage.OutputTokens,
            usage.CacheReadInputTokens,
            usage.CacheCreationInputTokens));
    }
}
