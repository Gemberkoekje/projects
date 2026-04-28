using FluentAssertions;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Domain.Tests.Events;

public sealed class ShipChainEventsTests
{
    [Fact]
    public void ShipUndockedEvent_WithEmptyCorrelation_UsesEventIdAsCorrelation()
    {
        var occurredAt = DateTimeOffset.UtcNow;

        var @event = new ShipUndockedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.Empty,
            Guid.Empty,
            occurredAt);

        @event.EventId.Should().NotBe(Guid.Empty);
        @event.CorrelationId.Should().Be(@event.EventId);
        @event.CausationId.Should().Be(Guid.Empty);
        @event.OccurredAt.Should().Be(occurredAt);
    }

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
}
