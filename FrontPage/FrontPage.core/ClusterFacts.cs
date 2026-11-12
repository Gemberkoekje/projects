namespace FrontPage.core;

/// <summary>What the cluster reported at one moment, before it is summarised for the page.</summary>
public sealed record ClusterFacts
{
    /// <summary>Every node.</summary>
    public required IReadOnlyList<NodeFacts> Nodes { get; init; }

    /// <summary>The namespace of every running pod, one entry per pod.</summary>
    public required IReadOnlyList<string> RunningPodNamespaces { get; init; }

    /// <summary>Every Deployment and StatefulSet.</summary>
    public required IReadOnlyList<WorkloadFacts> Workloads { get; init; }

    /// <summary>The gembernodes commit Flux has, or <see cref="GitVersion.Unknown"/>.</summary>
    public required GitVersion Version { get; init; }
}

/// <summary>One node as the cluster reports it.</summary>
public sealed record NodeFacts
{
    /// <summary>The node name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether its Ready condition is True.</summary>
    public required bool Ready { get; init; }

    /// <summary>The kubelet version, such as <c>v1.37.2+k3s1</c>.</summary>
    public required string KubeletVersion { get; init; }

    /// <summary>What pods may use on the node.</summary>
    public required ResourceAmounts Allocatable { get; init; }

    /// <summary>What is in use now, or <see cref="ResourceAmounts.Unknown"/> without metrics.</summary>
    public required ResourceAmounts Usage { get; init; }
}

/// <summary>A Deployment or StatefulSet and how many of its replicas are ready.</summary>
public sealed record WorkloadFacts
{
    /// <summary>Where it lives.</summary>
    public required WorkloadRef Workload { get; init; }

    /// <summary>Its ready replicas.</summary>
    public required int ReadyReplicas { get; init; }
}

/// <summary>An amount of CPU (in cores) and memory (in bytes).</summary>
public readonly record struct ResourceAmounts
{
    /// <summary>Stands in for amounts that could not be read.</summary>
    public static ResourceAmounts Unknown { get; } = new ResourceAmounts { Cpu = -1, Memory = -1 };

    /// <summary>CPU in cores.</summary>
    public required decimal Cpu { get; init; }

    /// <summary>Memory in bytes.</summary>
    public required decimal Memory { get; init; }

    /// <summary>Whether both amounts were read.</summary>
    public bool IsKnown => Cpu >= 0 && Memory >= 0;
}
