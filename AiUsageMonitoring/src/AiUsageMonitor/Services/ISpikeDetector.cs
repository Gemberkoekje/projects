using AiUsageMonitor.Models;
using System.Threading;
using System.Threading.Tasks;

namespace AiUsageMonitor.Services;

public interface ISpikeDetector
{
    Task<SpikeDetectionResult> DetectAndRecordAsync(BurndownReport report, CancellationToken cancellationToken = default);
}
