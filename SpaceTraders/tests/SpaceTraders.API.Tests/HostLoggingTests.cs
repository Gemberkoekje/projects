using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Health;

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
}
