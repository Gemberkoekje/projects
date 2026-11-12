using k8s;
using k8s.Autorest;
using k8s.Models;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace FrontPage.infrastructure;

/// <summary>
/// Reads the cluster through the Kubernetes API, as the app's own ServiceAccount: list nodes, pods,
/// Deployments, StatefulSets and node metrics, and get the <c>flux-system</c> GitRepository.
/// </summary>
/// <remarks>
/// Nodes, pods and workloads are required: without them there is nothing to show. Node metrics and
/// the Flux revision are optional; when they fail, only those fields are left empty.
/// </remarks>
public sealed class KubernetesClusterFactsReader(IKubernetes kubernetes, ILogger<KubernetesClusterFactsReader> logger)
    : IClusterFactsReader
{
    private const string FluxNamespace = "flux-system";
    private const string FluxGitRepository = "flux-system";

    /// <inheritdoc />
    public async Task<Result<ClusterFacts>> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var nodes = await kubernetes.CoreV1.ListNodeAsync(cancellationToken: cancellationToken);
            var pods = await kubernetes.CoreV1.ListPodForAllNamespacesAsync(fieldSelector: "status.phase=Running", cancellationToken: cancellationToken);
            var deployments = await kubernetes.AppsV1.ListDeploymentForAllNamespacesAsync(cancellationToken: cancellationToken);
            var statefulSets = await kubernetes.AppsV1.ListStatefulSetForAllNamespacesAsync(cancellationToken: cancellationToken);
            var usage = await ReadUsageAsync(cancellationToken);
            var version = await ReadVersionAsync(cancellationToken);

            return new ClusterFacts
            {
                Nodes = [.. nodes.Items.Select(n => ToFacts(n, usage))],
                RunningPodNamespaces = [.. pods.Items.Select(p => p.Metadata.NamespaceProperty)],
                Workloads =
                [
                    .. deployments.Items.Select(d => Workload(d.Metadata, d.Status?.ReadyReplicas)),
                    .. statefulSets.Items.Select(s => Workload(s.Metadata, s.Status?.ReadyReplicas)),
                ],
                Version = version,
            };
        }
        catch (Exception ex) when (IsClusterFailure(ex))
        {
            logger.LogError(ex, "Could not read the cluster");
            return Result.WithMessages<ClusterFacts>(ValidationMessage.Error("The cluster could not be read."));
        }
    }

    private async Task<Dictionary<string, ResourceAmounts>> ReadUsageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var metrics = await kubernetes.GetKubernetesNodesMetricsAsync().WaitAsync(cancellationToken);
            return metrics.Items.ToDictionary(m => m.Metadata.Name, m => Amounts(m.Usage), StringComparer.Ordinal);
        }
        catch (Exception ex) when (IsClusterFailure(ex))
        {
            logger.LogWarning(ex, "Node metrics unavailable; CPU and memory are left out");
            return new Dictionary<string, ResourceAmounts>(StringComparer.Ordinal);
        }
    }

    private async Task<GitVersion> ReadVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var repository = await kubernetes.CustomObjects.GetNamespacedCustomObjectAsync<JsonElement>(
                group: "source.toolkit.fluxcd.io",
                version: "v1",
                namespaceParameter: FluxNamespace,
                plural: "gitrepositories",
                name: FluxGitRepository,
                cancellationToken: cancellationToken);

            return repository.TryGetProperty("status", out var status)
                && status.TryGetProperty("artifact", out var artifact)
                && artifact.TryGetProperty("revision", out var revision)
                && artifact.TryGetProperty("lastUpdateTime", out var fetched)
                && fetched.TryGetDateTimeOffset(out var fetchedAt)
                ? GitVersion.FromFluxRevision(revision.GetString() ?? string.Empty, fetchedAt)
                : GitVersion.Unknown;
        }
        catch (Exception ex) when (IsClusterFailure(ex))
        {
            logger.LogWarning(ex, "Flux revision unavailable; the running version is left out");
            return GitVersion.Unknown;
        }
    }

    private static NodeFacts ToFacts(V1Node node, Dictionary<string, ResourceAmounts> usage)
        => new NodeFacts
        {
            Name = node.Metadata.Name,
            Ready = node.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") ?? false,
            KubeletVersion = node.Status?.NodeInfo?.KubeletVersion ?? string.Empty,
            Allocatable = node.Status?.Allocatable is { } allocatable ? Amounts(allocatable) : ResourceAmounts.Unknown,
            Usage = usage.GetValueOrDefault(node.Metadata.Name, ResourceAmounts.Unknown),
        };

    private static WorkloadFacts Workload(V1ObjectMeta metadata, int? readyReplicas)
        => new WorkloadFacts
        {
            Workload = new WorkloadRef { Namespace = metadata.NamespaceProperty, Name = metadata.Name },
            ReadyReplicas = readyReplicas ?? 0,
        };

    private static ResourceAmounts Amounts(IDictionary<string, ResourceQuantity> quantities)
        => quantities.TryGetValue("cpu", out var cpu) && quantities.TryGetValue("memory", out var memory)
            ? new ResourceAmounts { Cpu = cpu.ToDecimal(), Memory = memory.ToDecimal() }
            : ResourceAmounts.Unknown;

    /// <summary>The ways the cluster can fail to answer; anything else is a bug and should surface.</summary>
    private static bool IsClusterFailure(Exception ex)
        => ex is HttpOperationException or HttpRequestException or KubernetesException or JsonException or FormatException
            || (ex is TaskCanceledException && ex.InnerException is TimeoutException);
}
