namespace AdventureEngine.Infrastructure.Anthropic;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; init; } = string.Empty;
}
