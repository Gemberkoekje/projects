using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Health;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;

namespace SpaceTraders.API.Tests;

/// <summary>
/// B41: a host's log lines went to the process-wide <c>Serilog.Log.Logger</c>, which every host
/// replaces when it starts and closes when it stops. Tests run hosts side by side, so a host could log
/// into another host's sinks, or into none, and <c>HealthRuleScenarioTests</c>' RepeatingError
/// scenario failed on CI (main at 41a7c22): its warnings never reached its own error log.
/// </summary>
public sealed class HostLoggingTests
{
    [Fact]
    public async Task AHostsWarnings_ReachItsOwnErrorLog_AfterAnotherHostCameAndWent()
    {
        await using var host = new SpaceTradersApiFactory();
        _ = host.Services;

        await using (var other = new SpaceTradersApiFactory())
        {
            _ = other.Services;
        }

        var logger = host.Services.GetRequiredService<ILogger<GameLoopService>>();
        logger.LogWarning("HostLoggingTests: a warning for the first host.");

        host.Services.GetRequiredService<ErrorLog>().Since(DateTimeOffset.MinValue)
            .Should().ContainSingle(statement => statement.Statement.Contains("a warning for the first host", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALineWhoseExceptionCameFromTheServersReset_IsLeftOut_OthersStay()
    {
        // B93: one ResetDetected warning tells the reset; the calls it failed are left out (ServerResetLogFilter).
        await using var host = new SpaceTradersApiFactory();
        var logger = host.Services.GetRequiredService<ILogger<GameLoopService>>();

        logger.LogError(
            new SpaceTradersApiException(
                "SpaceTraders API request to 'my/ships/SPECTER-1F9/sell' failed with status code 503: The universe is being reset. Please check back in a few minutes.",
                HttpStatusCode.ServiceUnavailable,
                "my/ships/SPECTER-1F9/sell",
                null),
            "HostLoggingTests: a sale the reset failed.");
        logger.LogError(new InvalidOperationException("Something else."), "HostLoggingTests: another failure.");

        host.Services.GetRequiredService<ErrorLog>().Since(DateTimeOffset.MinValue)
            .Should().ContainSingle(statement => statement.Statement.Contains("HostLoggingTests", StringComparison.Ordinal))
            .Which.Statement.Should().Contain("another failure");
    }

    [Fact]
    public async Task TheHealthChecksWarnings_AreLeftOut()
    {
        // B93: the startup probe's "still running" (Degraded) logged a warning every 10 seconds of every start.
        await using var host = new SpaceTradersApiFactory();
        var logger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService");

        logger.LogWarning("HostLoggingTests: Health check \"startup\" with status Degraded.");
        logger.LogError("HostLoggingTests: Health check \"postgresql\" with status Unhealthy.");

        host.Services.GetRequiredService<ErrorLog>().Since(DateTimeOffset.MinValue)
            .Should().ContainSingle(statement => statement.Statement.Contains("HostLoggingTests", StringComparison.Ordinal))
            .Which.Statement.Should().Contain("Unhealthy");
    }
}
