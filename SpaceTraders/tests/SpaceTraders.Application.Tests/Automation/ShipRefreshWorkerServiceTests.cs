using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class ShipRefreshWorkerServiceTests
{
    private static ShipModel MakeShip(string symbol, string waypoint) =>
        new(
            symbol,
            SystemSymbol: "X1-AB",
            WaypointSymbol: waypoint,
            Status: "IN_ORBIT",
            FlightMode: "CRUISE",
            FuelCurrent: 0,
            FuelCapacity: 0);

    [Fact]
    public async Task TickAsync_RefreshesMarketAndShipyard_ForEachUniqueWaypoint()
    {
        var leaderElection = Substitute.For<ILeaderElection>();
        leaderElection.IsLeader.Returns(true);

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ShipModel>
            {
                MakeShip("SHIP-1", "X1-AB-001"),
                MakeShip("SHIP-2", "X1-AB-002"),
                MakeShip("SHIP-3", "X1-AB-001"), // duplicate waypoint
            });

        var marketRefresh = Substitute.For<IMarketRefreshService>();
        var shipyardRefresh = Substitute.For<IShipyardRefreshService>();

        var services = new ServiceCollection();
        services.AddSingleton(ships);
        services.AddSingleton(marketRefresh);
        services.AddSingleton(shipyardRefresh);

        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var worker = new ShipRefreshWorkerService(
            scopeFactory,
            leaderElection,
            NullLogger<ShipRefreshWorkerService>.Instance);

        await worker.TickAsync(CancellationToken.None);

        await marketRefresh.Received(1).RefreshIfApplicableAsync("X1-AB-001", Arg.Any<CancellationToken>());
        await marketRefresh.Received(1).RefreshIfApplicableAsync("X1-AB-002", Arg.Any<CancellationToken>());
        await marketRefresh.ReceivedWithAnyArgs(2).RefreshIfApplicableAsync(default!, default);

        await shipyardRefresh.Received(1).RefreshIfApplicableAsync("X1-AB-001", Arg.Any<CancellationToken>());
        await shipyardRefresh.Received(1).RefreshIfApplicableAsync("X1-AB-002", Arg.Any<CancellationToken>());
        await shipyardRefresh.ReceivedWithAnyArgs(2).RefreshIfApplicableAsync(default!, default);
    }

    [Fact]
    public async Task TickAsync_SkipsRefresh_WhenNotLeader()
    {
        var leaderElection = Substitute.For<ILeaderElection>();
        leaderElection.IsLeader.Returns(false);

        var ships = Substitute.For<IShipRepository>();
        var marketRefresh = Substitute.For<IMarketRefreshService>();
        var shipyardRefresh = Substitute.For<IShipyardRefreshService>();

        var services = new ServiceCollection();
        services.AddSingleton(ships);
        services.AddSingleton(marketRefresh);
        services.AddSingleton(shipyardRefresh);

        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var worker = new ShipRefreshWorkerService(
            scopeFactory,
            leaderElection,
            NullLogger<ShipRefreshWorkerService>.Instance);

        await worker.TickAsync(CancellationToken.None);

        await ships.DidNotReceiveWithAnyArgs().GetAllAsync(default);
        await marketRefresh.DidNotReceiveWithAnyArgs().RefreshIfApplicableAsync(default!, default);
        await shipyardRefresh.DidNotReceiveWithAnyArgs().RefreshIfApplicableAsync(default!, default);
    }

    [Fact]
    public async Task TickAsync_HandlesNoShips_WithoutError()
    {
        var leaderElection = Substitute.For<ILeaderElection>();
        leaderElection.IsLeader.Returns(true);

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ShipModel>());

        var marketRefresh = Substitute.For<IMarketRefreshService>();
        var shipyardRefresh = Substitute.For<IShipyardRefreshService>();

        var services = new ServiceCollection();
        services.AddSingleton(ships);
        services.AddSingleton(marketRefresh);
        services.AddSingleton(shipyardRefresh);

        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var worker = new ShipRefreshWorkerService(
            scopeFactory,
            leaderElection,
            NullLogger<ShipRefreshWorkerService>.Instance);

        var act = async () => await worker.TickAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await marketRefresh.DidNotReceiveWithAnyArgs().RefreshIfApplicableAsync(default!, default);
    }
}
