using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SpaceTraders.API.Services;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.API.Tests.Services;

/// <summary>Slice 2.13 (D70): the run's reset date on every metric and every log line, so runs never mix in Grafana.</summary>
public sealed class ResetDateLabelTests
{
    [Theory]
    [InlineData("SPECTER@2026-10-04", "2026-10-04")]
    [InlineData("GEMBER@2026-09-27", "2026-09-27")]
    [InlineData("", "")]
    public void Of_ReadsTheResetDate_FromTheAgentId(string agentId, string resetDate)
        => ResetDateLabel.Of(agentId).Should().Be(resetDate);

    [Fact]
    public void ALogLine_CarriesTheResetDate_OnceTheAgentIsKnown()
    {
        var agent = new AgentDataScope();
        var lines = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .Enrich.With(new ResetDateEnricher(agent))
            .WriteTo.Sink(new CollectingSink(lines))
            .CreateLogger();

        logger.Information("Deferred startup initialization started after the HTTP host came online.");
        agent.Set("SPECTER@2026-10-04");
        logger.Information("PlanStarted: Scout plan for ship {ShipSymbol}.", "SPECTER-1");

        lines.Should().HaveCount(2);
        lines[0].Properties.Should().NotContainKey(ResetDateLabel.LogProperty, "bootstrap hasn't picked the agent yet");
        lines[1].Properties[ResetDateLabel.LogProperty].ToString().Should().Be("\"2026-10-04\"");
    }

    private sealed class CollectingSink(List<LogEvent> lines) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => lines.Add(logEvent);
    }
}
