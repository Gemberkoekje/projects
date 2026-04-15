namespace Sts2Viewer.Llm;

public sealed class LlmResult
{
    public string Content { get; set; }

    public LlmResult()
    {
        Content = string.Empty;
    }
}
