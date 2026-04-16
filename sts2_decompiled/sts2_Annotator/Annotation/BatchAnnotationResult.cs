namespace Sts2Extractor.Annotation;

internal sealed class BatchAnnotationResult
{
    public string CustomId { get; set; }

    /// <summary>Non-null when the request succeeded.</summary>
    public AnnotationResult Result { get; set; }

    /// <summary>Non-null when the request failed or was errored by the provider.</summary>
    public string ErrorMessage { get; set; }

    public bool IsSuccess => Result != null;

    public BatchAnnotationResult()
    {
        CustomId = string.Empty;
        Result = null;
        ErrorMessage = string.Empty;
    }
}
