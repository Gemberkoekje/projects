using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.API.Hubs;

/// <summary>
/// Implements <see cref="IDashboardNotifier"/> by broadcasting invalidation events to all
/// connected <see cref="DashboardHub"/> clients via <see cref="IHubContext{THub,T}"/>.
/// Exceptions are swallowed and logged so that a SignalR failure never propagates to
/// the calling event handler.
/// </summary>
public sealed class DashboardNotifier(
    IHubContext<DashboardHub, IDashboardHubClient> hubContext,
    ILogger<DashboardNotifier> logger) : IDashboardNotifier
{
    public void Notify(string kind, string id)
    {
        var evt = new InvalidationEvent(kind, id, TimeProvider.System.GetUtcNow());
        // Fire-and-forget: all exceptions are caught inside SendAsync so none go unobserved.
        _ = SendAsync(evt);
    }

    private async Task SendAsync(InvalidationEvent evt)
    {
        try
        {
            await hubContext.Clients.All.ReceiveInvalidation(evt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send SignalR invalidation event {Kind}/{Id}", evt.Kind, evt.Id);
        }
    }
}
