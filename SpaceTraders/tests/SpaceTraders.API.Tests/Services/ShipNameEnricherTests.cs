using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.API.Tests.Services;

/// <summary>
/// Slice 2.14 (D72): every log line about a ship carries the name the bot gives it, beside its symbol, so the journal reads
/// SPUTNIK-2 where the game says SPECTER-4.
/// </summary>
public sealed class ShipNameEnricherTests
{
    private readonly List<LogEvent> _lines = [];
    private readonly ShipNameBook _names;

    public ShipNameEnricherTests()
    {
        var reset = Substitute.For<IActiveReset>();
        reset.ResetDate.Returns("2026-10-04");
        _names = new ShipNameBook(reset);
        _names.Know([Probe("SPECTER-2"), Probe("SPECTER-4")]);
    }

    [Fact]
    public void ALineNamingAShip_CarriesItsName()
    {
        using var logger = Logger();

        logger.Information("{EventKind:l}: ship {ShipSymbol} arrived at {WaypointSymbol}.", "Arrived", "SPECTER-4", "X1-FJ91-A1");

        _lines.Should().ContainSingle().Which.Properties[ShipNameEnricher.LogProperty].ToString()
            .Should().Be($"\"{_names.NameOf("SPECTER-4")}\"").And.EndWith("-2\"");
    }

    [Fact]
    public void ALineInAShipsGoalStep_CarriesItsName_ThoughItsMessageNamesNoShip()
    {
        // The tick runs each ship's goal step in a scope with its ShipSymbol (GameLoopService), through Microsoft's logging, as
        // the host does.
        using var serilog = Logger();
        using var factory = new SerilogLoggerFactory(serilog);
        var logger = factory.CreateLogger("SpaceTraders.Application.Automation.GameLoopService");

        using (logger.BeginScope(new Dictionary<string, object> { ["ShipSymbol"] = "SPECTER-2" }))
        {
            logger.LogInformation("Waiting for extraction cooldown.");
        }

        _lines.Should().ContainSingle().Which.Properties[ShipNameEnricher.LogProperty].ToString()
            .Should().Be($"\"{_names.NameOf("SPECTER-2")}\"");
    }

    [Fact]
    public void ALineAboutNoShip_OrAShipTheBookDoesntKnow_CarriesNoName()
    {
        using var logger = Logger();

        logger.Information("Deferred startup initialization started after the HTTP host came online.");
        logger.Information("{EventKind:l}: ship {ShipSymbol} bought.", "ShipPurchased", "SPECTER-9");

        _lines.Should().HaveCount(2).And.OnlyContain(line => !line.Properties.ContainsKey(ShipNameEnricher.LogProperty));
    }

    private Logger Logger()
        => new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.With(new ShipNameEnricher(_names))
            .WriteTo.Sink(new CollectingSink(_lines))
            .CreateLogger();

    private static ShipModel Probe(string symbol) => new(symbol, "X1-FJ91", "X1-FJ91-A1", "DOCKED", "CRUISE", 0, 0, ShipType: "SHIP_PROBE");

    private sealed class CollectingSink(List<LogEvent> lines) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => lines.Add(logEvent);
    }
}
