using System.Globalization;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Services;

/// <summary>
/// When the server resets next (PLAN.md slice 6.70, D150), as startup read it from the server's status
/// (<c>serverResets.next</c>) and stored it in <see cref="Setting"/>. A reset ends the run: the agent, its ships and its credits
/// are gone, so what a ship or a trade would earn after it counts for nothing.
/// </summary>
/// <remarks>Read afresh at each call; the setting changes only at a start.</remarks>
public sealed class ServerReset(ISettingsRepository settings)
{
    /// <summary>The status flag that holds the next reset, as an ISO 8601 time; empty while startup hasn't stored one.</summary>
    public const string Setting = "Runtime.Reset.Next";

    /// <summary>
    /// The next reset after <paramref name="now"/>: <see cref="DateTimeOffset.MaxValue"/> when none is known, or when the one
    /// stored has passed (the server put it off), so that no end-of-run rule applies.
    /// </summary>
    /// <param name="now">The time to judge by.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The next reset, or <see cref="DateTimeOffset.MaxValue"/>.</returns>
    public async Task<DateTimeOffset> NextAfterAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stored = await settings.GetRawAsync(Setting, cancellationToken);
        return DateTimeOffset.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var next) && next > now
            ? next
            : DateTimeOffset.MaxValue;
    }
}
