namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Pushes lightweight cache-invalidation events to connected dashboard clients (SignalR).
/// Implementations fire-and-forget; failures must not propagate to the calling handler.
/// </summary>
public interface IDashboardNotifier
{
    /// <summary>
    /// Notifies all connected dashboard clients that the entity identified by
    /// <paramref name="kind"/> and <paramref name="id"/> has changed and should be re-fetched.
    /// </summary>
    void Notify(string kind, string id);
}
