using NSubstitute;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipInTransitEventHandlerTests
{
    [Fact]
    public async Task Handle_NotifiesDashboard_WhenArrivalIsInFuture()
    {
        var dashboardNotifier = Substitute.For<IDashboardNotifier>();
        var handler = new ShipInTransitEventHandler(dashboardNotifier);

        var now = DateTimeOffset.UtcNow;
        var @event = new ShipInTransitEvent(
            "SHIP-1",
            "X1-AB-001",
            "X1-AB-002",
            now.AddMinutes(2),
            Guid.NewGuid(),
            Guid.Empty,
            now);

        await handler.Handle(@event, CancellationToken.None);

        dashboardNotifier.Received(1).Notify("ships", "SHIP-1");
        dashboardNotifier.Received(1).Notify("fleet-activity", "SHIP-1");
        dashboardNotifier.Received(1).Notify("activity", "SHIP-1");
    }

    [Fact]
    public async Task Handle_NotifiesDashboard_WhenArrivalIsDue()
    {
        var dashboardNotifier = Substitute.For<IDashboardNotifier>();
        var handler = new ShipInTransitEventHandler(dashboardNotifier);

        var now = DateTimeOffset.UtcNow;
        var @event = new ShipInTransitEvent(
            "SHIP-1",
            "X1-AB-001",
            "X1-AB-002",
            now.AddSeconds(-1),
            Guid.NewGuid(),
            Guid.Empty,
            now.AddMinutes(-2));

        await handler.Handle(@event, CancellationToken.None);

        dashboardNotifier.Received(1).Notify("ships", "SHIP-1");
        dashboardNotifier.Received(1).Notify("fleet-activity", "SHIP-1");
        dashboardNotifier.Received(1).Notify("activity", "SHIP-1");
    }
}
