using SpaceTraders.Application.Interfaces;
using Wolverine;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Wolverine middleware that counts every message handled without an error, by message type
/// (<c>spacetraders_messages_handled_total{type}</c>). It runs for published events and for
/// commands run inline alike.
/// </summary>
public static class MessageMetricsMiddleware
{
    /// <summary>Runs after the handlers, only when none of them threw.</summary>
    public static void After(Envelope envelope, IAutomationMetrics metrics)
    {
        metrics.MessageHandled(envelope.Message?.GetType().Name ?? envelope.MessageType ?? "unknown");
    }
}
