namespace FrontPage.application;

/// <summary>What <see cref="ClusterStatus"/> summarises the cluster against.</summary>
public sealed record ClusterStatusSettings
{
    /// <summary>The tools on the page and their workloads.</summary>
    public required IReadOnlyCollection<AppDefinition> Apps { get; init; }

    /// <summary>Namespaces that don't count as apps.</summary>
    public required IReadOnlySet<string> SystemNamespaces { get; init; }
}
