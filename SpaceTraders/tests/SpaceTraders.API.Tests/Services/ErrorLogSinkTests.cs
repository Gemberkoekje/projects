using FluentAssertions;
using Serilog;
using Serilog.Context;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Health;

namespace SpaceTraders.API.Tests.Services;

/// <summary>Phase 3.2: every warning and error reaches the RepeatingError rule, by log statement.</summary>
public sealed class ErrorLogSinkTests : IDisposable
{
    private readonly ErrorLog _errors = new();
    private readonly Serilog.Core.Logger _logger;

    public ErrorLogSinkTests()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new ErrorLogSink(_errors))
            .CreateLogger();
    }

    [Fact]
    public void AWarning_IsRecordedUnderItsClassAndTemplate()
    {
        _logger.ForContext("SourceContext", "SpaceTraders.Application.Automation.ContractPlanService")
            .Warning("Contract plan advance: deliverable {TradeSymbol} not found for contract {ContractId}.", "IRON_ORE", "C-1");

        var statement = _errors.Since(DateTimeOffset.MinValue).Should().ContainSingle().Which;
        statement.Statement.Should().Be("ContractPlanService: Contract plan advance: deliverable {TradeSymbol} not found for contract {ContractId}.");
        statement.LastMessage.Should().Be("Contract plan advance: deliverable \"IRON_ORE\" not found for contract \"C-1\".");
    }

    [Fact]
    public void ATemplateThatNamesItsClass_IsntPrefixedTwice()
    {
        _logger.ForContext("SourceContext", "SpaceTraders.Application.Automation.GameLoopService")
            .Error("GameLoopService: the goal step for ship {ShipSymbol} failed; the rest of the tick carries on.", "SHIP-1");

        _errors.Since(DateTimeOffset.MinValue).Should().ContainSingle()
            .Which.Statement.Should().Be("GameLoopService: the goal step for ship {ShipSymbol} failed; the rest of the tick carries on.");
    }

    [Fact]
    public void InformationAndDebug_StayOut()
    {
        _logger.Information("Ship {ShipSymbol} docked.", "SHIP-1");
        _logger.Debug("Nothing to do.");

        _errors.Since(DateTimeOffset.MinValue).Should().BeEmpty();
    }

    [Fact]
    public void JournalLines_StayOut()
    {
        // AnomalyRaised, ShipBlocked, PlanBlocked: what they report, other rules watch.
        _logger.Warning("{EventKind}: {Rule} on {Subject}: {Details}.", "AnomalyRaised", "ShipStuck", "SHIP-1", "stuck");

        using (LogContext.PushProperty("EventKind", "ShipBlocked"))
        {
            _logger.Warning("Ship {ShipSymbol} blocked.", "SHIP-1");
        }

        _errors.Since(DateTimeOffset.MinValue).Should().BeEmpty();
    }

    public void Dispose() => _logger.Dispose();
}
