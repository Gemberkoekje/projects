using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Planning;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Tests.EventHandlers;

public sealed class ShipAutomationTickEventHandlerTests
{
    [Fact]
    public async Task Handle_DelegatesToPlannerService()
    {
        var planner = Substitute.For<IShipPlannerService>();
        planner.PlanAndExecuteAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(ShipPlannerDecision.None("SHIP-1", "no-op"));

        var handler = new ShipAutomationTickEventHandler(planner, NullLogger<ShipAutomationTickEventHandler>.Instance);

        var @event = new ShipAutomationTickEvent(
            "SHIP-1",
            "test-reason",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.Empty);

        await handler.Handle(@event, CancellationToken.None);

        await planner.Received(1).PlanAndExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ShipAutomationTickEvent_IsNotPartOfChainOfCommandHierarchy()
    {
        var eventType = typeof(ShipAutomationTickEvent);

        eventType.IsSubclassOf(typeof(SpaceTraders.Domain.Events.ChainOfCommandEvent))
            .Should().BeFalse(
                "Phase 5 requires automation events to be explicit and factual, not routed via chain-of-command inheritance.");
    }
}
