using FrontPage.infrastructure;
using k8s;
using Microsoft.Extensions.Logging.Abstractions;

namespace KubernetesClusterFactsReader_specs;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class Reading_the_cluster : IDisposable
{
    private readonly StubKubernetesApi api = new StubKubernetesApi();

    [Test]
    public async Task maps_nodes_with_their_ready_condition_allocatable_and_version()
    {
        var facts = await Read();

        facts.Nodes.Should().BeEquivalentTo(new[]
        {
            new { Name = "gembernode-02", Ready = true, KubeletVersion = "v1.37.2+k3s1", Allocatable = new { Cpu = 4m, Memory = 8m * 1024 * 1024 * 1024 } },
            new { Name = "gembernode-03", Ready = false, KubeletVersion = "v1.37.2+k3s1", Allocatable = new { Cpu = 4m, Memory = 8m * 1024 * 1024 * 1024 } },
        });
    }

    [Test]
    public async Task attaches_metrics_to_the_node_they_belong_to()
    {
        var facts = await Read();

        facts.Nodes.Single(n => n.Name == "gembernode-02").Usage.Should().Be(new ResourceAmounts { Cpu = 0.513m, Memory = 2m * 1024 * 1024 * 1024 });
        facts.Nodes.Single(n => n.Name == "gembernode-03").Usage.Should().Be(ResourceAmounts.Unknown);
    }

    [Test]
    public async Task asks_only_for_running_pods()
    {
        var facts = await Read();

        facts.RunningPodNamespaces.Should().Equal("kluskat", "zeef");
        api.Requests.Should().Contain(r => r.StartsWith(StubKubernetesApi.Pods, StringComparison.Ordinal) && r.Contains("fieldSelector=status.phase%3DRunning", StringComparison.Ordinal));
    }

    [Test]
    public async Task maps_deployments_and_statefulsets_with_missing_ready_counts_as_zero()
    {
        var facts = await Read();

        facts.Workloads.Should().BeEquivalentTo(new[]
        {
            Facts.Workload("kluskat/kluskat", 1),
            Facts.Workload("zeef/jellyfin", 0),
            Facts.Workload("flux-system/postgresql", 1),
        });
    }

    [Test]
    public async Task reads_the_commit_flux_has_fetched()
    {
        var facts = await Read();

        facts.Version.Should().BeEquivalentTo(new
        {
            Commit = "6969711fe18fc30117c18c8f371b490dec7d224c",
            FetchedAt = new DateTimeOffset(2026, 11, 12, 03, 34, 54, TimeSpan.Zero),
        });
    }

    [Test]
    public async Task without_metrics_still_reads_the_rest()
    {
        api.Break(StubKubernetesApi.NodeMetrics);

        var facts = await Read();

        facts.Nodes.Should().AllSatisfy(n => n.Usage.Should().Be(ResourceAmounts.Unknown));
        facts.Workloads.Should().HaveCount(3);
    }

    [Test]
    public async Task without_the_flux_revision_still_reads_the_rest()
    {
        api.Break(StubKubernetesApi.FluxGitRepository);

        var facts = await Read();

        facts.Version.Should().BeSameAs(GitVersion.Unknown);
        facts.Nodes.Should().HaveCount(2);
    }

    [TestCase(StubKubernetesApi.Nodes)]
    [TestCase(StubKubernetesApi.Pods)]
    [TestCase(StubKubernetesApi.Deployments)]
    [TestCase(StubKubernetesApi.StatefulSets)]
    public async Task fails_when_a_required_list_cannot_be_read(string path)
    {
        api.Break(path);
        using var kubernetes = Kubernetes();

        var result = await new KubernetesClusterFactsReader(kubernetes, NullLogger<KubernetesClusterFactsReader>.Instance).ReadAsync(CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    private async Task<ClusterFacts> Read()
    {
        using var kubernetes = Kubernetes();
        var result = await new KubernetesClusterFactsReader(kubernetes, NullLogger<KubernetesClusterFactsReader>.Instance).ReadAsync(CancellationToken.None);
        result.IsValid.Should().BeTrue();
        return result.Value;
    }

    public void Dispose() => api.Dispose();

    private Kubernetes Kubernetes()
        => new Kubernetes(new KubernetesClientConfiguration { Host = "http://cluster.test" }, api);
}
