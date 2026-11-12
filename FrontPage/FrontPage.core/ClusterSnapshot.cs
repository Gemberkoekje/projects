namespace FrontPage.core;

/// <summary>The summary of the cluster the page shows. This is everything that leaves the app.</summary>
public sealed record ClusterSnapshot
{
    /// <summary>What the page gets before the first successful read.</summary>
    public static ClusterSnapshot Unavailable { get; } = new ClusterSnapshot
    {
        Available = false,
        GeneratedAt = DateTimeOffset.MinValue,
        Nodes = [],
        PodsRunning = 0,
        AppsRunning = 0,
        Version = GitVersion.Unknown,
        Apps = new Dictionary<string, AppState>(),
    };

    /// <summary>False until the first successful read.</summary>
    public required bool Available { get; init; }

    /// <summary>When the facts behind it were read.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Every node, by name.</summary>
    public required IReadOnlyList<NodeSnapshot> Nodes { get; init; }

    /// <summary>How many pods are running.</summary>
    public required int PodsRunning { get; init; }

    /// <summary>How many app namespaces have a running pod.</summary>
    public required int AppsRunning { get; init; }

    /// <summary>The gembernodes commit Flux has.</summary>
    public required GitVersion Version { get; init; }

    /// <summary>Each configured tool, by its page id.</summary>
    public required IReadOnlyDictionary<string, AppState> Apps { get; init; }
}

/// <summary>One node as the page shows it.</summary>
public sealed record NodeSnapshot
{
    /// <summary>The node name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether it is Ready.</summary>
    public required bool Ready { get; init; }

    /// <summary>The kubelet version, such as <c>v1.37.2+k3s1</c>.</summary>
    public required string Version { get; init; }

    /// <summary>Whether the two percentages were measured.</summary>
    public required bool UsageKnown { get; init; }

    /// <summary>CPU in use, as a whole percentage of what is allocatable.</summary>
    public required int CpuPercent { get; init; }

    /// <summary>Memory in use, as a whole percentage of what is allocatable.</summary>
    public required int MemoryPercent { get; init; }
}
