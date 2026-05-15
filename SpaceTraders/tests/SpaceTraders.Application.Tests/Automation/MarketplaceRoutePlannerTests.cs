using FluentAssertions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class MarketplaceRoutePlannerTests
{
    [Fact]
    public async Task BuildRouteAsync_StartWaypointInCandidates_IsFirstInRoute()
    {
        var planner = new MarketplaceRoutePlanner();
        var candidates = new List<WaypointCacheModel>
        {
            Market("X1-AB-003", 20, 0),
            Market("X1-AB-001", 0, 0),
            Market("X1-AB-002", 10, 0),
        };

        var route = await planner.BuildRouteAsync("X1-AB-002", candidates, CancellationToken.None);

        route.Should().Equal("X1-AB-002", "X1-AB-001", "X1-AB-003");
    }

    [Fact]
    public async Task BuildRouteAsync_StartWaypointMissing_ReturnsDeterministicOrderedRoute()
    {
        var planner = new MarketplaceRoutePlanner();
        var candidates = new List<WaypointCacheModel>
        {
            Market("X1-AB-003", 20, 0),
            Market("X1-AB-001", 0, 0),
            Market("X1-AB-002", 10, 0),
        };

        var route = await planner.BuildRouteAsync("X1-AB-999", candidates, CancellationToken.None);

        route.Should().Equal("X1-AB-001", "X1-AB-002", "X1-AB-003");
    }

    [Fact]
    public async Task BuildRouteAsync_DeduplicatesCandidateSymbols()
    {
        var planner = new MarketplaceRoutePlanner();
        var candidates = new List<WaypointCacheModel>
        {
            Market("X1-AB-001", 0, 0),
            Market("X1-AB-001", 100, 100),
            Market("X1-AB-002", 10, 0),
        };

        var route = await planner.BuildRouteAsync("X1-AB-001", candidates, CancellationToken.None);

        route.Should().Equal("X1-AB-001", "X1-AB-002");
    }

    [Fact]
    public async Task BuildRouteAsync_UsesNearestNeighbor_ByWaypointCoordinates()
    {
        var planner = new MarketplaceRoutePlanner();
        var candidates = new List<WaypointCacheModel>
        {
            Market("X1-AB-START", 0, 0),
            Market("X1-AB-NEAR", 1, 0),
            Market("X1-AB-MID", 4, 0),
            Market("X1-AB-FAR", 9, 0),
        };

        var route = await planner.BuildRouteAsync("X1-AB-START", candidates, CancellationToken.None);

        route.Should().Equal("X1-AB-START", "X1-AB-NEAR", "X1-AB-MID", "X1-AB-FAR");
    }

    [Fact]
    public async Task BuildRouteAsync_TieBreaksBySymbol_WhenDistancesAreEqual()
    {
        var planner = new MarketplaceRoutePlanner();
        var candidates = new List<WaypointCacheModel>
        {
            Market("X1-AB-START", 0, 0),
            Market("X1-AB-B", 1, 0),
            Market("X1-AB-A", 0, 1),
        };

        var route = await planner.BuildRouteAsync("X1-AB-START", candidates, CancellationToken.None);

        route.Should().Equal("X1-AB-START", "X1-AB-A", "X1-AB-B");
    }

    private static WaypointCacheModel Market(string symbol, int x, int y) =>
        new(
            Symbol: symbol,
            SystemSymbol: "X1-AB",
            Type: "PLANET",
            X: x,
            Y: y,
            HasMarket: true,
            HasShipyard: false,
            LastObservedAt: DateTimeOffset.UtcNow);
}
