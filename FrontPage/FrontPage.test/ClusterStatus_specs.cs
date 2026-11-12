using Microsoft.Extensions.Logging.Abstractions;

namespace ClusterStatus_specs;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class Refreshing
{
    private readonly FakeClusterFactsReader reader = new FakeClusterFactsReader();
    private readonly FixedTime time = new FixedTime(Facts.Now);

    [Test]
    public void before_the_first_read_the_status_is_unavailable()
    {
        Status().Current.Should().BeSameAs(ClusterSnapshot.Unavailable);
    }

    [Test]
    public async Task replaces_the_summary_when_the_read_works()
    {
        var status = Status();
        reader.Next = Facts.Cluster() with { RunningPodNamespaces = ["kluskat"] };

        var result = await status.RefreshAsync(CancellationToken.None);

        result.IsValid.Should().BeTrue();
        status.Current.Should().BeEquivalentTo(new { Available = true, GeneratedAt = Facts.Now, PodsRunning = 1 });
    }

    [Test]
    public async Task keeps_the_previous_summary_when_the_read_fails()
    {
        var status = Status();
        await status.RefreshAsync(CancellationToken.None);
        var previous = status.Current;
        reader.Next = FakeClusterFactsReader.Failure();
        time.Now = Facts.Now.AddMinutes(5);

        var result = await status.RefreshAsync(CancellationToken.None);

        result.IsValid.Should().BeFalse();
        status.Current.Should().BeSameAs(previous);
        status.Current.GeneratedAt.Should().Be(Facts.Now);
    }

    [Test]
    public async Task stays_unavailable_when_the_first_read_fails()
    {
        var status = Status();
        reader.Next = FakeClusterFactsReader.Failure();

        await status.RefreshAsync(CancellationToken.None);

        status.Current.Available.Should().BeFalse();
    }

    private ClusterStatus Status()
        => new ClusterStatus(
            reader,
            new ClusterStatusSettings { Apps = [Facts.App("kluskat", "kluskat/kluskat")], SystemNamespaces = new HashSet<string>() },
            time,
            NullLogger<ClusterStatus>.Instance);
}
