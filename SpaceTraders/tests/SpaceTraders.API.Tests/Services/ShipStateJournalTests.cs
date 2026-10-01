using FluentAssertions;
using Microsoft.Extensions.Logging;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.API.Tests.Services;

/// <summary>Slice 2.3: the journal says when a ship turns idle, once per spell, with a reason.</summary>
public sealed class ShipStateJournalTests
{
    private readonly JournalLogger _log = new();
    private readonly ShipStateJournal _journal;

    public ShipStateJournalTests()
    {
        _journal = new ShipStateJournal(_log);
    }

    [Fact]
    public void AtTheFirstSample_EachIdleShipIsJournaled_AndNoOther()
    {
        _journal.Observe([Ship("AGENT-1", "None"), Ship("AGENT-2", "ScoutWaypoint")]);

        _log.Lines.Should().Equal("ShipIdle: ship AGENT-1 is idle (idle_at_start): no goal and no assignment.");
    }

    [Fact]
    public void AShipWhoseGoalEnds_IsJournaledOnce()
    {
        _journal.Observe([Ship("AGENT-2", "ScoutWaypoint")]);
        _journal.Observe([Ship("AGENT-2", "None")]);
        _journal.Observe([Ship("AGENT-2", "None")]);

        _log.Lines.Should().Equal("ShipIdle: ship AGENT-2 is idle (goal_ended): its ScoutWaypoint ended.");
    }

    [Fact]
    public void AShipThatAppearsIdleLater_IsANewShip()
    {
        _journal.Observe([Ship("AGENT-1", "Contract")]);
        _journal.Observe([Ship("AGENT-1", "Contract"), Ship("AGENT-3", "None")]);

        _log.Lines.Should().Equal("ShipIdle: ship AGENT-3 is idle (new_ship): no goal and no assignment.");
    }

    private static ShipMetricsSample Ship(string symbol, string goal) => new(symbol, "COMMAND", "DOCKED", goal, string.Empty);

    private sealed class JournalLogger : ILogger<ShipStateJournal>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> properties && properties.Any(p => p.Key == "EventKind"))
            {
                Lines.Add(formatter(state, exception));
            }
        }
    }
}
