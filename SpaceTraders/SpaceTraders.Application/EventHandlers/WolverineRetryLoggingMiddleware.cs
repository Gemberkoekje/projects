using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Wolverine;
using Wolverine.Runtime;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Wolverine middleware that logs a warning whenever an event or message is retried.
/// This provides visibility into retry behavior and helps diagnose transient failures.
/// </summary>
public static class WolverineRetryLoggingMiddleware
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Finally(Envelope envelope, ILogger logger)
    {
        if (envelope.Attempts > 1)
        {
            logger.LogWarning(
                "Wolverine retry #{RetryAttempt} for message type {MessageType} with envelope ID {EnvelopeId}",
                envelope.Attempts,
                envelope.MessageType,
                envelope.Id);
        }
    }
}
