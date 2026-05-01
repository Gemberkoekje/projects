using FluentAssertions;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Domain.Tests.Events;

public sealed class ShipChainEventsTests
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
}
