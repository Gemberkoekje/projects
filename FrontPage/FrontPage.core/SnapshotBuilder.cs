namespace FrontPage.core;

/// <summary>Turns what the cluster reported into the summary the page shows.</summary>
public static class SnapshotBuilder
{
    /// <summary>Summarises the facts.</summary>
    /// <param name="facts">What the cluster reported.</param>
    /// <param name="apps">The tools on the page and their workloads.</param>
    /// <param name="systemNamespaces">Namespaces that don't count as apps.</param>
    /// <param name="generatedAt">When the facts were read.</param>
    /// <returns>The summary.</returns>
    public static ClusterSnapshot Build(
        ClusterFacts facts,
        IReadOnlyCollection<AppDefinition> apps,
        IReadOnlySet<string> systemNamespaces,
        DateTimeOffset generatedAt)
    {
        // A Deployment and a StatefulSet may share a name; the best of the two counts.
        var readyByWorkload = facts.Workloads
            .GroupBy(w => w.Workload)
            .ToDictionary(g => g.Key, g => g.Max(w => w.ReadyReplicas));

        return new ClusterSnapshot
        {
            Available = true,
            GeneratedAt = generatedAt,
            Nodes = [.. facts.Nodes.OrderBy(n => n.Name, StringComparer.Ordinal).Select(Summarise)],
            PodsRunning = facts.RunningPodNamespaces.Count,
            AppsRunning = facts.RunningPodNamespaces
                .Where(ns => !systemNamespaces.Contains(ns))
                .Distinct(StringComparer.Ordinal)
                .Count(),
            Version = facts.Version,
            Apps = apps.ToDictionary(a => a.Id, a => StateOf(a, readyByWorkload), StringComparer.Ordinal),
        };
    }

    private static NodeSnapshot Summarise(NodeFacts node)
    {
        var known = node.Allocatable.IsKnown && node.Usage.IsKnown;
        return new NodeSnapshot
        {
            Name = node.Name,
            Ready = node.Ready,
            Version = node.KubeletVersion,
            UsageKnown = known,
            CpuPercent = known ? Percentage(node.Usage.Cpu, node.Allocatable.Cpu) : 0,
            MemoryPercent = known ? Percentage(node.Usage.Memory, node.Allocatable.Memory) : 0,
        };
    }

    /// <summary>A whole percentage, half rounded up, between 0 and 100.</summary>
    private static int Percentage(decimal used, decimal available)
        => available <= 0
            ? 0
            : (int)Math.Clamp(decimal.Floor((used * 100 / available) + 0.5m), 0, 100);

    private static AppState StateOf(AppDefinition app, Dictionary<WorkloadRef, int> readyByWorkload)
        => app.Workloads.Count > 0 && app.Workloads.All(w => readyByWorkload.TryGetValue(w, out var ready) && ready > 0)
            ? AppState.Up
            : AppState.Down;
}
