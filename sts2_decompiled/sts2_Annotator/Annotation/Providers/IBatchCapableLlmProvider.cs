using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2Extractor.Annotation.Providers;

/// <summary>
/// Implemented by providers that support submitting many requests in a single API call
/// (e.g. Anthropic Message Batches).  Results are returned asynchronously after polling.
/// </summary>
internal interface IBatchCapableLlmProvider : ILlmProvider
{
    /// <summary>
    /// Submits all <paramref name="items"/> as one provider-side batch, polls until
    /// complete, and returns results keyed by <see cref="BatchAnnotationItem.CustomId"/>.
    /// </summary>
    Task<IReadOnlyDictionary<string, BatchAnnotationResult>> BatchAnnotateAsync(
        IReadOnlyList<BatchAnnotationItem> items,
        CancellationToken cancellationToken);
}
