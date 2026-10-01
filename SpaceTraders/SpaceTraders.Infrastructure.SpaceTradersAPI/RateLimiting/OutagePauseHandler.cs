using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>
/// Handles a 502, which comes from the API's DDoS protection, the way the API guide asks: "wait a
/// few minutes before trying again". After a 502 no call goes out for
/// <c>Api.BadGatewayPauseMinutes</c> (default 3); calls in that time fail at once with
/// <see cref="ApiPausedException"/>. The first call after the pause goes out as usual: a success
/// marks the API available again, another 502 starts another pause.
/// </summary>
public sealed class OutagePauseHandler(
    IApiAvailabilityState availability,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<OutagePauseHandler> logger) : DelegatingHandler
{
    public const string PauseMinutesSetting = "Api.BadGatewayPauseMinutes";

    public const int DefaultPauseMinutes = 3;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pausedUntil = availability.PausedUntil;
        if (TimeProvider.System.GetUtcNow() < pausedUntil)
        {
            throw new ApiPausedException(pausedUntil);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadGateway)
        {
            var minutes = await ReadPauseMinutesAsync();
            availability.PauseUntil(TimeProvider.System.GetUtcNow().AddMinutes(minutes));
        }
        else
        {
            availability.MarkAvailable();
        }

        return response;
    }

    private async Task<int> ReadPauseMinutesAsync()
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            var minutes = await settings.GetAsync<int>(PauseMinutesSetting, CancellationToken.None);
            return minutes > 0 ? minutes : DefaultPauseMinutes;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read {Setting}; pausing API calls for {Minutes} minutes.", PauseMinutesSetting, DefaultPauseMinutes);
            return DefaultPauseMinutes;
        }
    }
}

/// <summary>A call made while API calls are paused after a 502 (see <see cref="OutagePauseHandler"/>).</summary>
public sealed class ApiPausedException(DateTimeOffset pausedUntil)
    : Exception($"SpaceTraders API calls are paused until {pausedUntil:O} after a 502 from its DDoS protection.")
{
    public DateTimeOffset PausedUntil { get; } = pausedUntil;
}
