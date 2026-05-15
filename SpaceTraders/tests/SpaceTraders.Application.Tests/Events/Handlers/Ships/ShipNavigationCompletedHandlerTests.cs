using NSubstitute;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipNavigationCompletedHandlerTests
{
    [Fact]
    public async Task Handle_NotifiesDashboard_WhenNavigationCompletes()
    {
        var goalExecutor = Substitute.For<IShipGoalExecutorService>();
        goalExecutor.ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((GoalExecutionResult?)null);

        var dashboardNotifier = Substitute.For<IDashboardNotifier>();
        var handler = new ShipNavigationCompletedHandler(goalExecutor, dashboardNotifier, Substitute.For<Microsoft.Extensions.Logging.ILogger<ShipNavigationCompletedHandler>>());

        await handler.Handle(new ShipNavigationCompletedEvent("SHIP-1", "X1-AB-002", Guid.NewGuid()), CancellationToken.None);

        dashboardNotifier.Received(1).Notify("ships", "SHIP-1");
        dashboardNotifier.Received(1).Notify("fleet-activity", "SHIP-1");
        dashboardNotifier.Received(1).Notify("activity", "SHIP-1");
        dashboardNotifier.Received(1).Notify("fleet-assignments", "SHIP-1");
        dashboardNotifier.Received(1).Notify("fleet-goal-chains", "SHIP-1");
    }
}
