#pragma warning disable IDISP001, IDISP004

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class GameLoopServiceTests
{
    [Fact]
    public async Task Tick_ExecutesGoalExecutor_ForAllShipsNotJustScouts()
    {
        var serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
        using var serviceProvider = new ServiceCollection()
            .AddSingleton(Substitute.For<IMessageBus>())
            .AddSingleton(Substitute.For<IScoutAllMarketplacesPlanService>())
            .AddSingleton(Substitute.For<IContractPlanService>())
            .AddSingleton(Substitute.For<IProbeDeploymentPlanService>())
            .AddSingleton(Substitute.For<IMiningAutomationService>())
            .AddSingleton(Substitute.For<ITradingAutomationService>())
            .AddSingleton(Substitute.For<IShipAssignmentRepository>())
            .AddSingleton(Substitute.For<IShipRepository>())
            .AddSingleton(Substitute.For<IShipGoalExecutorService>())
            .BuildServiceProvider();

        serviceScopeFactory.CreateScope().Returns(_ => serviceProvider.CreateScope());

        var assignments = serviceProvider.GetRequiredService<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var ships = serviceProvider.GetRequiredService<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("SPECTER-DEBUG-3", "X1-PT96", "X1-PT96-H53", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 15),
            new ShipModel("SPECTER-DEBUG-5", "X1-PT96", "X1-PT96-D42", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 40),
        ]);

        var goalExecutor = serviceProvider.GetRequiredService<IShipGoalExecutorService>();

        var apiAvailability = Substitute.For<IApiAvailabilityState>();
        apiAvailability.ConsumeUnavailableTransition().Returns(false);
        apiAvailability.ConsumeAvailableTransition().Returns(false);

        var leaderElection = Substitute.For<ILeaderElection>();
        leaderElection.IsLeader.Returns(true);

        using var sut = new GameLoopService(
            serviceScopeFactory,
            apiAvailability,
            leaderElection,
            NullLogger<GameLoopService>.Instance);

        var tickMethod = typeof(GameLoopService)
            .GetMethod("TickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)tickMethod.Invoke(sut, [CancellationToken.None])!;

        await goalExecutor.Received().ExecuteAsync("SPECTER-DEBUG-3", Arg.Any<CancellationToken>());
        await goalExecutor.Received().ExecuteAsync("SPECTER-DEBUG-5", Arg.Any<CancellationToken>());
    }
}
