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

    /// <summary>
    /// Phase 7f: ChainOfCommandEvent has been deleted. Assert it no longer exists in the domain assembly
    /// as a regression guard (prevents accidental re-introduction).
    /// </summary>
    [Fact]
    public void Phase7f_ChainOfCommandEvent_NoLongerExistsInDomainAssembly()
    {
        var domainAssembly = typeof(ShipAutomationTickEvent).Assembly;

        var chainType = domainAssembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == "ChainOfCommandEvent");

        chainType.Should().BeNull(
            "Phase 7f deleted ChainOfCommandEvent; it must not be re-introduced in the domain assembly.");
    }
}
