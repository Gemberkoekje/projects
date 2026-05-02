using FluentAssertions;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Domain.Tests.Events;

public sealed class ShipEventsTests
{
    [Fact]
    public void ShipInTransitEvent_PreservesProvidedCorrelationAndPayload()
    {
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var departure = DateTimeOffset.UtcNow;
        var arrival = departure.AddMinutes(2);

        var @event = new ShipInTransitEvent(
            "SHIP-1",
            "X1-AB-001",
            "X1-AB-002",
            arrival,
            correlationId,
            causationId,
            departure);

        @event.CorrelationId.Should().Be(correlationId);
        @event.CausationId.Should().Be(causationId);
        @event.ShipSymbol.Should().Be("SHIP-1");
        @event.OriginWaypointSymbol.Should().Be("X1-AB-001");
        @event.DestinationWaypointSymbol.Should().Be("X1-AB-002");
        @event.ArrivalTime.Should().Be(arrival);
    }

    [Fact]
    public void ShipStateMismatchEvent_PreservesProvidedCorrelationAndPayload()
    {
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var @event = new ShipStateMismatchEvent(
            "SHIP-1",
            "DockShipCommand",
            "IN_ORBIT",
            "DOCKED",
            "Ship must be in orbit before docking.",
            correlationId,
            causationId,
            occurredAt);

        @event.CorrelationId.Should().Be(correlationId);
        @event.CausationId.Should().Be(causationId);
        @event.OccurredAt.Should().Be(occurredAt);
        @event.ShipSymbol.Should().Be("SHIP-1");
        @event.CommandName.Should().Be("DockShipCommand");
        @event.RequiredState.Should().Be("IN_ORBIT");
        @event.ActualState.Should().Be("DOCKED");
        @event.Reason.Should().Be("Ship must be in orbit before docking.");
    }

    [Fact]
    public void ConstructionSuppliedEvent_PreservesProvidedCorrelationAndPayload()
    {
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var @event = new ConstructionSuppliedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            "IRON_ORE",
            50,
            false,
            correlationId,
            causationId,
            occurredAt);

        @event.CorrelationId.Should().Be(correlationId);
        @event.CausationId.Should().Be(causationId);
        @event.OccurredAt.Should().Be(occurredAt);
        @event.ShipSymbol.Should().Be("SHIP-1");
        @event.SystemSymbol.Should().Be("X1-AB");
        @event.WaypointSymbol.Should().Be("X1-AB-001");
        @event.TradeSymbol.Should().Be("IRON_ORE");
        @event.UnitsSupplied.Should().Be(50);
        @event.IsComplete.Should().BeFalse();
    }
}
