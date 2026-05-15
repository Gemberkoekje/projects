using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class ScoutMarketplaceDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_ReturnsOnlyMarketWaypointsInStartingSystem()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns(
        [
            new WaypointCacheModel("X1-AB-003", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
            new WaypointCacheModel("X1-AB-002", "X1-AB", "ASTEROID", 0, 0, false, false, DateTimeOffset.UtcNow),
            new WaypointCacheModel("X1-AB-001", "X1-AB", "MOON", 0, 0, true, false, DateTimeOffset.UtcNow),
        ]);

        var service = new ScoutMarketplaceDiscoveryService(waypoints, NullLogger<ScoutMarketplaceDiscoveryService>.Instance);
        var ship = new ShipModel("SHIP-SCOUT", "X1-AB", "X1-AB-START", "DOCKED", "CRUISE", 80, 100);

        var markets = await service.DiscoverAsync(ship, CancellationToken.None);

        markets.Select(m => m.Symbol).Should().Equal("X1-AB-001", "X1-AB-003");
    }

    [Fact]
    public async Task DiscoverAsync_UsesWaypointSymbol_WhenSystemSymbolMissing()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        waypoints.GetBySystemAsync("X1-CD", Arg.Any<CancellationToken>()).Returns(
        [
            new WaypointCacheModel("X1-CD-001", "X1-CD", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
        ]);

        var service = new ScoutMarketplaceDiscoveryService(waypoints, NullLogger<ScoutMarketplaceDiscoveryService>.Instance);
        var ship = new ShipModel("SHIP-SCOUT", null, "X1-CD-START", "DOCKED", "CRUISE", 80, 100);

        var markets = await service.DiscoverAsync(ship, CancellationToken.None);

        markets.Should().HaveCount(1);
        await waypoints.Received(1).GetBySystemAsync("X1-CD", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscoverAsync_Throws_WhenStartingSystemCannotBeResolved()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var service = new ScoutMarketplaceDiscoveryService(waypoints, NullLogger<ScoutMarketplaceDiscoveryService>.Instance);
        var ship = new ShipModel("SHIP-SCOUT", null, null, "DOCKED", "CRUISE", 80, 100);

        var act = () => service.DiscoverAsync(ship, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no starting system symbol is available*");
    }
}
