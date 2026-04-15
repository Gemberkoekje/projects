using System.Threading;
using System.Threading.Tasks;

namespace Sts2Extractor.Annotation.Providers;

internal interface ILlmProvider
{
    Task<AnnotationResult> AnnotateAsync(AnnotationRequest request, CancellationToken cancellationToken);
}
