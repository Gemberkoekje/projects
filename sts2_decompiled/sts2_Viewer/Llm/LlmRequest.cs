namespace Sts2Viewer.Llm;

public sealed class LlmRequest
{
    public string Model { get; set; }

    public string SystemPrompt { get; set; }

    public string Prompt { get; set; }

    public int MaxTokens { get; set; }

    public LlmRequest()
    {
        Model = string.Empty;
        SystemPrompt = string.Empty;
        Prompt = string.Empty;
        MaxTokens = 500;
    }
}
