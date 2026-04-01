using AiUsageMonitor.Models;
using System.Threading;
using System.Threading.Tasks;

namespace AiUsageMonitor.Services;

public interface IBurndownCalculator
{
    Task<BurndownReport> CalculateBurndownAsync(string provider, long budget, CancellationToken cancellationToken = default);
}
