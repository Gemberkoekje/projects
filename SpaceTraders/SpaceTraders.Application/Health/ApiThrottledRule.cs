using System.Globalization;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Health;

/// <summary>
/// At most one 429 an hour for every N ships (PLAN.md 3.2, D149): the SpaceTraders API answered no more calls with 429 in
/// the last hour, retries included, than the fleet's ships divided by <c>Health.Api.ShipsPer429PerHour</c> (10), rounded
/// down. The subject is <c>api</c>.
/// </summary>
/// <remarks>
/// The client keeps to the API guide's limits (slice 1.10), and the soak test (1.14) saw no 429 in four hours with a few
/// ships. A large fleet keeps the budget nearly full, and the API's rate limiter still refuses some requests however
/// closely the client keeps to it (B59): 6 to 37 an hour at about 600 ships on the night to 2026-11-15, all retried. So
/// a fleet of fewer than N ships is allowed none, and 500 ships 50. A 429 from the rate limiter (<c>rate_limiter</c>)
/// means the client's reading of the limits is off; one from the cloud infrastructure (<c>infrastructure</c>) comes
/// from outside.
/// </remarks>
public sealed class ApiThrottledRule(ApiResponseLog responses, IShipRepository ships, ISettingsRepository settings)
    : IHealthRule
{
    /// <summary>The setting that holds how many ships each 429 an hour is allowed for.</summary>
    public const string Setting = "Health.Api.ShipsPer429PerHour";

    /// <summary>The number when the setting gives none.</summary>
    public const int DefaultShipsPer429 = 10;

    private const string RateLimiterSource = "rate_limiter";

    /// <inheritdoc />
    public string Name => "ApiThrottled";

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var throttled = responses.Since(context.Now - ApiResponseLog.Window).Where(response => response.StatusCode == 429).ToList();
        if (throttled.Count == 0)
        {
            return [];
        }

        var shipsPer429 = await settings.ThresholdAsync(Setting, DefaultShipsPer429, cancellationToken);
        var fleet = (await ships.GetAllAsync(cancellationToken)).Count;
        var limit = fleet / shipsPer429;
        if (throttled.Count <= limit)
        {
            return [];
        }

        var fromRateLimiter = throttled.Count(response => response.Source == RateLimiterSource);
        return
        [
            new HealthViolation(
                "api",
                string.Create(CultureInfo.InvariantCulture,
                    $"the SpaceTraders API answered 429 Too Many Requests {throttled.Count} times in the last hour, {fromRateLimiter} from its rate limiter and {throttled.Count - fromRateLimiter} from its infrastructure; limit {limit}, one for every {shipsPer429} of the {fleet} ships ({Setting})")),
        ];
    }
}
