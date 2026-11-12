namespace SnapshotBuilder_specs;

public class Nodes
{
    [Test]
    public void are_ordered_by_name()
    {
        var facts = Facts.Cluster() with { Nodes = [Facts.Node("gembernode-03"), Facts.Node("gembernode-01"), Facts.Node("gembernode-02")] };

        Build(facts).Nodes.Select(n => n.Name).Should().Equal("gembernode-01", "gembernode-02", "gembernode-03");
    }

    [Test]
    public void show_usage_as_whole_percentages_of_what_is_allocatable()
    {
        var facts = Facts.Cluster() with { Nodes = [Facts.Node("n", cpuUsed: 0.513m, memoryUsed: 3.94m)] };

        var node = Build(facts).Nodes.Single();

        node.Should().BeEquivalentTo(new { UsageKnown = true, CpuPercent = 13, MemoryPercent = 49 });
    }

    [Test]
    public void round_half_a_percent_up()
    {
        var facts = Facts.Cluster() with { Nodes = [Facts.Node("n", cpuUsed: 0.5m, memoryUsed: 0.04m)] };

        Build(facts).Nodes.Single().Should().BeEquivalentTo(new { CpuPercent = 13, MemoryPercent = 1 });
    }

    [Test]
    public void never_show_more_than_100_percent()
    {
        var facts = Facts.Cluster() with { Nodes = [Facts.Node("n", cpuUsed: 5, memoryUsed: 9)] };

        Build(facts).Nodes.Single().Should().BeEquivalentTo(new { CpuPercent = 100, MemoryPercent = 100 });
    }

    [Test]
    public void without_metrics_say_so_instead_of_showing_zero_as_a_measurement()
    {
        var facts = Facts.Cluster() with { Nodes = [Facts.Node("n", usageKnown: false)] };

        Build(facts).Nodes.Single().Should().BeEquivalentTo(new { UsageKnown = false, CpuPercent = 0, MemoryPercent = 0 });
    }

    [Test]
    public void keep_ready_and_the_kubelet_version()
    {
        var facts = Facts.Cluster() with { Nodes = [Facts.Node("n", ready: false)] };

        Build(facts).Nodes.Single().Should().BeEquivalentTo(new { Ready = false, Version = "v1.37.2+k3s1" });
    }

    private static ClusterSnapshot Build(ClusterFacts facts)
        => SnapshotBuilder.Build(facts, [], new HashSet<string>(), Facts.Now);
}

public class Counts
{
    [Test]
    public void pods_running_is_every_running_pod()
    {
        var facts = Facts.Cluster() with { RunningPodNamespaces = ["zeef", "zeef", "kluskat", "kube-system"] };

        Build(facts).PodsRunning.Should().Be(4);
    }

    [Test]
    public void apps_running_counts_each_namespace_once_and_leaves_out_system_namespaces()
    {
        var facts = Facts.Cluster() with { RunningPodNamespaces = ["zeef", "zeef", "zeef", "kluskat", "kube-system", "traefik"] };

        Build(facts).AppsRunning.Should().Be(2);
    }

    private static ClusterSnapshot Build(ClusterFacts facts)
        => SnapshotBuilder.Build(facts, [], new HashSet<string> { "kube-system", "traefik" }, Facts.Now);
}

public class Apps
{
    [Test]
    public void are_up_when_every_workload_has_a_ready_replica()
    {
        var facts = Facts.Cluster() with { Workloads = [Facts.Workload("ebi/api", 1), Facts.Workload("ebi/frontend", 2)] };

        Build(facts, Facts.App("ebi", "ebi/api", "ebi/frontend")).Apps["ebi"].Should().Be(AppState.Up);
    }

    [Test]
    public void are_down_when_one_workload_has_no_ready_replica()
    {
        var facts = Facts.Cluster() with { Workloads = [Facts.Workload("ebi/api", 1), Facts.Workload("ebi/frontend", 0)] };

        Build(facts, Facts.App("ebi", "ebi/api", "ebi/frontend")).Apps["ebi"].Should().Be(AppState.Down);
    }

    [Test]
    public void are_down_when_a_workload_does_not_exist()
    {
        var facts = Facts.Cluster() with { Workloads = [Facts.Workload("ebi/api", 1)] };

        Build(facts, Facts.App("ebi", "ebi/api", "ebi/frontend")).Apps["ebi"].Should().Be(AppState.Down);
    }

    [Test]
    public void are_down_without_workloads_to_check()
    {
        Build(Facts.Cluster(), Facts.App("empty")).Apps["empty"].Should().Be(AppState.Down);
    }

    [Test]
    public void count_the_best_of_a_deployment_and_statefulset_with_the_same_name()
    {
        var facts = Facts.Cluster() with { Workloads = [Facts.Workload("db/postgres", 0), Facts.Workload("db/postgres", 1)] };

        Build(facts, Facts.App("db", "db/postgres")).Apps["db"].Should().Be(AppState.Up);
    }

    [Test]
    public void list_every_configured_app_and_nothing_else()
    {
        var facts = Facts.Cluster() with { Workloads = [Facts.Workload("a/a", 1), Facts.Workload("private/thing", 1)] };

        Build(facts, Facts.App("a", "a/a"), Facts.App("b", "b/b")).Apps.Keys.Should().BeEquivalentTo("a", "b");
    }

    private static ClusterSnapshot Build(ClusterFacts facts, params AppDefinition[] apps)
        => SnapshotBuilder.Build(facts, apps, new HashSet<string>(), Facts.Now);
}

public class Snapshot
{
    [Test]
    public void is_available_and_carries_when_it_was_read_and_the_version()
    {
        var version = new GitVersion { Commit = new string('a', 40), FetchedAt = Facts.Now.AddHours(-3) };

        var snapshot = SnapshotBuilder.Build(Facts.Cluster() with { Version = version }, [], new HashSet<string>(), Facts.Now);

        snapshot.Should().BeEquivalentTo(new { Available = true, GeneratedAt = Facts.Now, Version = version });
    }
}
