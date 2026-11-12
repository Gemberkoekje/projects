namespace FrontPage.test.TestHelpers;

/// <summary>Builders for cluster facts in tests.</summary>
internal static class Facts
{
    public static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 12, 10, 00, 00, TimeSpan.Zero);

    /// <summary>One ready node and nothing else; adjust with <c>with { ... }</c>.</summary>
    public static ClusterFacts Cluster()
        => new ClusterFacts
        {
            Nodes = [Node("gembernode-01")],
            RunningPodNamespaces = [],
            Workloads = [],
            Version = GitVersion.Unknown,
        };

    public static NodeFacts Node(
        string name,
        bool ready = true,
        decimal cpuUsed = 1,
        decimal memoryUsed = 4,
        bool usageKnown = true)
        => new NodeFacts
        {
            Name = name,
            Ready = ready,
            KubeletVersion = "v1.37.2+k3s1",
            Allocatable = new ResourceAmounts { Cpu = 4, Memory = 8 },
            Usage = usageKnown ? new ResourceAmounts { Cpu = cpuUsed, Memory = memoryUsed } : ResourceAmounts.Unknown,
        };

    public static WorkloadFacts Workload(string workload, int ready)
        => new WorkloadFacts { Workload = WorkloadRef.Parse(workload), ReadyReplicas = ready };

    public static AppDefinition App(string id, params string[] workloads)
        => new AppDefinition { Id = id, Workloads = [.. workloads.Select(WorkloadRef.Parse)] };
}
