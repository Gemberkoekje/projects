using System.Globalization;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Health;

/// <summary>
/// At most N 429s an hour (PLAN.md 3.2): the SpaceTraders API answered no more than
/// <c>Health.Api.Max429sPerHour</c> calls (10) with 429 in the last hour, retries included. The subject
/// is <c>api</c>.
/// </summary>
/// <remarks>
/// The client keeps to the API guide's limits (slice 1.10), so none are expected: the soak test (1.14)
/// saw none in four hours. A 429 from the rate limiter (<c>rate_limiter</c>) means the client's
/// reading of the limits is off; one from the cloud infrastructure (<c>infrastructure</c>) comes from
/// outside. The limit leaves room for the occasional one.
/// </remarks>
public sealed class ApiThrottledRule(ApiResponseLog responses, ISettingsRepository settings) : IHealthRule
{
    /// <summary>The setting that holds how many 429s an hour are allowed.</summary>
    public const string Setting = "Health.Api.Max429sPerHour";

    /// <summary>The number when the setting gives none.</summary>
    public const int DefaultPerHour = 10;

    private const string RateLimiterSource = "rate_limiter";

    /// <inheritdoc />
    public string Name => "ApiThrottled";

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var throttled = responses.Since(context.Now - ApiResponseLog.Window).Where(response => response.StatusCode == 429).ToList();
        var limit = await settings.ThresholdAsync(Setting, DefaultPerHour, cancellationToken);
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
                    $"the SpaceTraders API answered 429 Too Many Requests {throttled.Count} times in the last hour, {fromRateLimiter} from its rate limiter and {throttled.Count - fromRateLimiter} from its infrastructure; limit {limit} ({Setting})")),
        ];
    }
}
