using Serilog.Events;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;

namespace SpaceTraders.API.Services;

/// <summary>
/// Leaves the lines out of the log whose exception is a call the server's reset failed
/// (<see cref="SpaceTradersApiException.CameFromTheReset"/>): the 503 it answers while it resets, or
/// an old token's 401 (B93).
/// </summary>
/// <remarks>
/// The first such call stops the host with one <c>ResetDetected</c> warning (<see cref="ServerResetMonitor"/>),
/// and bootstrap waits for the server after the restart. Until the host has stopped, the messages in
/// flight still fail on the reset, each with Wolverine's error line, and the handlers' own lines:
/// on 2026-11-15 some 60 Errors and Warnings between 13:00:22 and 13:00:42Z, each counted by the
/// "SpaceTraders logged errors" mail. They tell nothing the warning doesn't, so no sink gets them,
/// the <c>RepeatingError</c> rule's neither.
/// </remarks>
public static class ServerResetLogFilter
{
    /// <summary>Whether <paramref name="logEvent"/> is left out: its exception came from the reset.</summary>
    /// <param name="logEvent">The line.</param>
    /// <returns>True when the line's exception, or one it holds, is a call the reset failed.</returns>
    public static bool Excludes(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        return logEvent.Exception is { } exception && SpaceTradersApiException.CameFromTheReset(exception);
    }
}
