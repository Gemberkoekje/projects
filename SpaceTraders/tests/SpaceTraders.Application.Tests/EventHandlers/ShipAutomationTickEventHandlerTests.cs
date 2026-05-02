using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Goals;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Tests.EventHandlers;

public sealed class ShipAutomationTickEventHandlerTests
{
    [Fact]
    public async Task Handle_DelegatesToGoalExecutorService()
    {
        var goalExecutorService = Substitute.For<IShipGoalExecutorService>();
        goalExecutorService.ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns((GoalExecutionResult?)null);

        var handler = new ShipAutomationTickEventHandler(goalExecutorService, NullLogger<ShipAutomationTickEventHandler>.Instance);

        var @event = new ShipAutomationTickEvent(
            "SHIP-1",
            "test-reason",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.Empty);

        await handler.Handle(@event, CancellationToken.None);

        await goalExecutorService.Received(1).ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
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
