namespace SpaceTraders.Application.Ports;

/// <summary>
/// The API's rate limit as the bot's own budget has it now (PLAN.md slice 6.41, D125): how much of the burst, the requests
/// allowed beyond 2 a second, is left. The burst refills as requests leave its 60-second window, so this is read at the moment
/// it is needed, not from the last request's snapshot.
/// </summary>
/// <remarks>Implemented by the rate limit's budget, a singleton; thread-safe.</remarks>
public interface IRequestPressure
{
    /// <summary>The burst requests the API allows beyond 2 a second, per 60 seconds.</summary>
    int BurstSize { get; }

    /// <summary>The burst requests still unused at <paramref name="now"/>.</summary>
    /// <param name="now">The time to judge by.</param>
    /// <returns>Between 0 and <see cref="BurstSize"/>.</returns>
    int BurstRemaining(DateTimeOffset now);
}
