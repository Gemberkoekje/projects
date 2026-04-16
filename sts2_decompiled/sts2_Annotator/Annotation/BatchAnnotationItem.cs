namespace Sts2Extractor.Annotation;

internal sealed class BatchAnnotationItem
{
    /// <summary>Unique identifier that will be echoed back in the batch result.</summary>
    public string CustomId { get; set; }

    public AnnotationRequest Request { get; set; }

    public BatchAnnotationItem()
    {
        CustomId = string.Empty;
        Request = new AnnotationRequest();
    }
}
