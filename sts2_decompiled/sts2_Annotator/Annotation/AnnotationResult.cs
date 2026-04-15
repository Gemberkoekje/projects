namespace Sts2Extractor.Annotation;

internal sealed class AnnotationResult
{
    public string Provider { get; set; }

    public string Model { get; set; }

    public string Content { get; set; }

    public AnnotationResult()
    {
        Provider = string.Empty;
        Model = string.Empty;
        Content = string.Empty;
    }
}
