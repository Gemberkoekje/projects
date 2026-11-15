using System.Net;
using FluentAssertions;
using Serilog.Events;
using SpaceTraders.API.Services;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;

namespace SpaceTraders.API.Tests.Services;

/// <summary>
/// B93: on 2026-11-15 the messages in flight when the server began its reset failed on it, some 60
/// Errors and Warnings in 20 seconds, each counted by the "SpaceTraders logged errors" mail. One
/// <c>ResetDetected</c> warning tells it; the lines whose exception came from the reset are left out.
/// </summary>
public sealed class ServerResetLogFilterTests
{
    [Fact]
    public void ALineFailedByTheResetsAnswer_IsLeftOut()
    {
        ServerResetLogFilter.Excludes(Line(LogEventLevel.Error, ResetInProgress())).Should().BeTrue();
    }

    [Fact]
    public void ALineFailedByAnOldTokensAnswer_IsLeftOut()
    {
        var oldToken = new SpaceTradersApiException(
            "SpaceTraders API request to 'my/ships/SPECTER-1E7/purchase' failed with status code 401: Failed to parse token. Token reset_date does not match the server.",
            HttpStatusCode.Unauthorized,
            "my/ships/SPECTER-1E7/purchase",
            null);

        ServerResetLogFilter.Excludes(Line(LogEventLevel.Warning, oldToken)).Should().BeTrue();
    }

    [Fact]
    public void TheResetsAnswer_IsFoundInsideOtherExceptions()
    {
        var wrapped = new InvalidOperationException("A step failed.", new AggregateException(new TimeoutException(), ResetInProgress()));

        ServerResetLogFilter.Excludes(Line(LogEventLevel.Error, wrapped)).Should().BeTrue();
    }

    [Fact]
    public void OtherLines_Stay()
    {
        var otherOutage = new SpaceTradersApiException(
            "SpaceTraders API request to 'my/ships' failed with status code 503.",
            HttpStatusCode.ServiceUnavailable,
            "my/ships",
            null);
        var notDocked = new SpaceTradersApiException(
            "SpaceTraders API request to 'my/ships/SPECTER-1/sell' failed with status code 400: Ship is not docked.",
            HttpStatusCode.BadRequest,
            "my/ships/SPECTER-1/sell",
            null);

        ServerResetLogFilter.Excludes(Line(LogEventLevel.Error, otherOutage)).Should().BeFalse();
        ServerResetLogFilter.Excludes(Line(LogEventLevel.Error, notDocked)).Should().BeFalse();
        ServerResetLogFilter.Excludes(Line(LogEventLevel.Warning, exception: null)).Should().BeFalse(
            "the ResetDetected line itself carries no exception");
    }

    private static SpaceTradersApiException ResetInProgress() => new(
        "SpaceTraders API request to 'my/ships/SPECTER-1F9/sell' failed with status code 503: The universe is being reset. Please check back in a few minutes.",
        HttpStatusCode.ServiceUnavailable,
        "my/ships/SPECTER-1F9/sell",
        null);

    private static LogEvent Line(LogEventLevel level, Exception? exception)
        => new(DateTimeOffset.UtcNow, level, exception, MessageTemplate.Empty, []);
}
