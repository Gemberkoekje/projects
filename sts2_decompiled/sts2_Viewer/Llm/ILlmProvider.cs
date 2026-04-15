using System.Threading;
using System.Threading.Tasks;

namespace Sts2Viewer.Llm;

public interface ILlmProvider
{
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
