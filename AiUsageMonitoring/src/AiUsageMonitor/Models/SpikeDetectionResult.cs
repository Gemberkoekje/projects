using AiUsageMonitor.Events;
using System.Collections.Generic;

namespace AiUsageMonitor.Models;

public sealed record SpikeDetectionResult
{
    public string Provider { get; init; } = string.Empty;
    public IReadOnlyList<AlertFired> FiredAlerts { get; init; } = [];
    public bool HasAlerts => FiredAlerts.Count > 0;
}
