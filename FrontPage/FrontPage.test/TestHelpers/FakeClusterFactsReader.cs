namespace FrontPage.test.TestHelpers;

/// <summary>Answers with whatever the test sets, and counts how often it was asked.</summary>
internal sealed class FakeClusterFactsReader : IClusterFactsReader
{
    public Result<ClusterFacts> Next { get; set; } = Facts.Cluster();

    public int Reads { get; private set; }

    public Task<Result<ClusterFacts>> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(Next);
    }

    public static Result<ClusterFacts> Failure()
        => Result.WithMessages<ClusterFacts>(ValidationMessage.Error("The cluster could not be read."));
}
