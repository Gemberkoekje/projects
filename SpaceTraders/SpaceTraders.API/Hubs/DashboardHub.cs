using Microsoft.AspNetCore.SignalR;

namespace SpaceTraders.API.Hubs;

/// <summary>
/// Typed client interface for the dashboard SignalR hub.
/// The server pushes <see cref="InvalidationEvent"/> envelopes; clients use them
/// as cache-invalidation signals and re-fetch the relevant REST endpoint.
/// </summary>
public interface IDashboardHubClient
{
    /// <summary>Receives a cache-invalidation envelope from the server.</summary>
    Task ReceiveInvalidation(InvalidationEvent @event);
}

/// <summary>
/// Lightweight invalidation envelope pushed to all connected dashboard clients.
/// </summary>
/// <param name="Kind">Entity kind (e.g. "agent-credits", "ship", "market", "run", "system-alert").</param>
/// <param name="Id">Entity identifier (ship symbol, waypoint symbol, run id, …).</param>
/// <param name="OccurredAt">UTC timestamp when the backend change was recorded.</param>
public sealed record InvalidationEvent(string Kind, string Id, DateTimeOffset OccurredAt);

/// <summary>
/// SignalR hub that pushes real-time cache-invalidation events to dashboard clients.
/// Clients subscribe by connecting; no explicit subscription methods are required for the
/// broadcast model used in Phase 0d.
/// </summary>
public sealed class DashboardHub : Hub<IDashboardHubClient>
{
}
