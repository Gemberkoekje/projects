namespace Klaverjas.application.Ports;

/// <summary>Keeps the one <see cref="OnlineStand"/>: what the players' page shows.</summary>
public interface IOnlineStandStore
{
    /// <summary>What is online, or an empty one when nothing has been put online yet.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The standings online.</returns>
    Task<OnlineStand> LadenAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces what is online.</summary>
    /// <param name="online">The new standings online.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once stored.</returns>
    Task OpslaanAsync(OnlineStand online, CancellationToken cancellationToken = default);
}
