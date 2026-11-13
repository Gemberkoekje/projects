using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.API.Tests;

/// <summary>
/// Slice 6.62 (D147): every caller that reads or writes a ship's goal gets the goals behind the ship's step guard, so a plan's
/// write waits for the ship's step, whichever plan, handler or endpoint makes it.
/// </summary>
public sealed class ShipGoalWritesTests
{
    [Fact]
    public async Task TheHost_GivesEveryCallerTheGoalsBehindTheStepGuard()
    {
        await using var host = new SpaceTradersApiFactory();
        await using var scope = host.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IShipGoalRepository>().Should().BeOfType<StepGuardedShipGoalRepository>();
    }
}
