using System.Globalization;

namespace SpaceTraders.Application.Health;

/// <summary>
/// No 401 or reset errors (PLAN.md 3.2): the SpaceTraders API answered no call with 401 in the last
/// hour since startup. The subject is <c>api</c>.
/// </summary>
/// <remarks>
/// A server reset (a 401 whose message says the token's reset date doesn't match) stops the host
/// (<c>ResetDetected</c>, slice 1.8), so what stays for this rule is a token the server rejects for
/// another reason: every call with it fails. Startup is left out: agent bootstrap tries old tokens on
/// purpose.
/// </remarks>
public sealed class ApiUnauthorizedRule(ApiResponseLog responses) : IHealthRule
{
    /// <inheritdoc />
    public string Name => "ApiUnauthorized";

    /// <inheritdoc />
    public Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var since = HealthCheckContext.Latest(context.Now - ApiResponseLog.Window, context.MonitorStartedAt);
        var unauthorized = responses.Since(since).Where(response => response.StatusCode == 401).ToList();
        if (unauthorized.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<HealthViolation>>([]);
        }

        var last = unauthorized[^1];
        return Task.FromResult<IReadOnlyList<HealthViolation>>(
        [
            new HealthViolation(
                "api",
                string.Create(CultureInfo.InvariantCulture,
                    $"the SpaceTraders API answered 401 Unauthorized {(unauthorized.Count == 1 ? "once" : $"{unauthorized.Count} times")} in the last hour, the last time for {last.Endpoint} at {last.At:u}")),
        ]);
    }
}
