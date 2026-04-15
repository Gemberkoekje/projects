namespace Sts2Extractor.Annotation;

internal sealed class AnnotationRequest
{
    public string Model { get; set; }

    public string Prompt { get; set; }

    public string SystemPrompt { get; set; }

    public int MaxTokens { get; set; }

    public AnnotationRequest()
    {
        Model = string.Empty;
        Prompt = string.Empty;
        SystemPrompt = string.Empty;
        MaxTokens = 1200;
    }
}
