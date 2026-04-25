namespace SpaceTraders.API.Middleware;

/// <summary>
/// Validates the <c>X-Api-Key</c> request header against the
/// <c>SPACETRADERS_INTERNAL_API_KEY</c> environment variable / configuration value.
/// Returns 401 on mismatch. Health endpoints (<c>/health/*</c>) are always allowed through.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ApiKeyConfigKey = "SPACETRADERS_INTERNAL_API_KEY";

    public async Task InvokeAsync(HttpContext context)
    {
        // Health probes must never require auth
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var configuredKey = configuration[ApiKeyConfigKey];

        // If no API key is configured, allow all requests (development convenience)
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey) ||
            !string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: valid X-Api-Key header required.");
            return;
        }

        await next(context);
    }
}
