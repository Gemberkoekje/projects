namespace FrontPage.application.Ports;

/// <summary>Reads what the page needs to know from the cluster.</summary>
public interface IClusterFactsReader
{
    /// <summary>Reads the facts.</summary>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The facts, or the reason they could not be read.</returns>
    Task<Result<ClusterFacts>> ReadAsync(CancellationToken cancellationToken);
}
