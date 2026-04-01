using AiUsageMonitor.Events;
using System.Threading;
using System.Threading.Tasks;

namespace AiUsageMonitor.Clients;

public interface ICopilotUsageClient
{
    Task<UsageSnapshotRecorded> GetLatestUsageAsync(CancellationToken cancellationToken = default);
}
