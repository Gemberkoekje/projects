namespace FrontPage.application;

/// <summary>
/// Holds the latest summary of the cluster. A background service refreshes it; requests only read
/// <see cref="Current"/>, so visitors never cause a call to the cluster.
/// </summary>
/// <remarks>
/// Thread safe: one refresher writes, any number of requests read, and the summary is immutable.
/// </remarks>
public sealed class ClusterStatus(
    IClusterFactsReader reader,
    ClusterStatusSettings settings,
    TimeProvider time,
    ILogger<ClusterStatus> logger)
{
    private ClusterSnapshot current = ClusterSnapshot.Unavailable;

    /// <summary>
    /// The latest summary. After a failed refresh this stays the previous one, whose
    /// <see cref="ClusterSnapshot.GeneratedAt"/> shows how old it is.
    /// </summary>
    public ClusterSnapshot Current => Volatile.Read(ref current);

    /// <summary>Reads the cluster and replaces <see cref="Current"/> when that works.</summary>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>OK, or why the cluster could not be read.</returns>
    public async Task<Result> RefreshAsync(CancellationToken cancellationToken)
    {
        var facts = await reader.ReadAsync(cancellationToken);
        if (!facts.IsValid)
        {
            logger.LogWarning("Cluster status not refreshed; keeping the summary from {GeneratedAt}", Current.GeneratedAt);
            return Result.WithMessages(facts.Messages);
        }

        var snapshot = SnapshotBuilder.Build(facts.Value, settings.Apps, settings.SystemNamespaces, time.GetUtcNow());
        Volatile.Write(ref current, snapshot);
        logger.LogDebug("Cluster status refreshed: {Nodes} nodes, {Pods} pods", snapshot.Nodes.Count, snapshot.PodsRunning);
        return Result.OK;
    }
}
