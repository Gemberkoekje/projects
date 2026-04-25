using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Application.Tests.Services;

public sealed class ShipAssignmentPlannerTests
{
    private static ISettingsRepository MakeSettings(int minProfit = 200, int maxDistance = 5, decimal miningPercentage = 0.25m)
    {
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("Trade.MinProfitPerUnit", Arg.Any<CancellationToken>()).Returns(minProfit);
        settings.GetAsync<int>("Trade.MaxHaulDistance", Arg.Any<CancellationToken>()).Returns(maxDistance);
        settings.GetAsync<int>("Scout.MarketRefreshIntervalMinutes", Arg.Any<CancellationToken>()).Returns(10);
        settings.GetAsync<decimal>("Automation.MiningShipPercentage", Arg.Any<CancellationToken>()).Returns(miningPercentage);
        return settings;
    }

    [Fact]
    public async Task PlanAsync_AssignsScout_WhenNoTradeRouteAvailable()
    {
        var routes = Substitute.For<ITradeOpportunityRepository>();
        var settings = MakeSettings();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var ships = Substitute.For<IShipRepository>();

        routes.GetBestRouteForCapacityAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((TradeOpportunityDto)null!);
        waypoints.GetUnscoutedOrStaleAsync("X1-AB", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns([new WaypointCacheModel("X1-AB-002", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow.AddHours(-1))]);

        var planner = new ShipAssignmentPlanner(routes, settings, assignments, waypoints, ships);
        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100, null);

        var assignment = await planner.PlanAsync(ship, CancellationToken.None);

        assignment.AssignmentType.Should().Be("Scout");
        assignment.OriginWaypoint.Should().Be("X1-AB-002");
    }

    [Fact]
    public async Task PlanAsync_AssignsSupplyChainTrade_WhenRouteAvailable()
    {
        var routes = Substitute.For<ITradeOpportunityRepository>();
        var settings = MakeSettings();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var ships = Substitute.For<IShipRepository>();

        routes.GetBestRouteForCapacityAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TradeOpportunityDto(1, "IRON_ORE", "X1-AB-001", "X1-AB-002", 100, 400, 300, 1, 300m, true, 1, DateTimeOffset.UtcNow));

        var planner = new ShipAssignmentPlanner(routes, settings, assignments, waypoints, ships);
        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100, null);

        var assignment = await planner.PlanAsync(ship, CancellationToken.None);

        assignment.AssignmentType.Should().Be("Trade");
        assignment.CargoSymbol.Should().Be("IRON_ORE");
    }

    [Fact]
    public async Task PlanAsync_AssignsMine_ForMiningShip_WhenQuotaNotMet()
    {
        var routes = Substitute.For<ITradeOpportunityRepository>();
        var settings = MakeSettings(miningPercentage: 0.5m);
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var ships = Substitute.For<IShipRepository>();

        routes.GetBestRouteForCapacityAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TradeOpportunityDto(1, "IRON_ORE", "X1-AB-003", "X1-AB-004", 100, 400, 300, 1, 300m, true, 1, DateTimeOffset.UtcNow));
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                new WaypointCacheModel("X1-AB-AST", "X1-AB", "ASTEROID_FIELD", 0, 0, false, false, DateTimeOffset.UtcNow),
                new WaypointCacheModel("X1-AB-MKT", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow)
            ]);

        var miningShip = new ShipModel(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            "DOCKED",
            "CRUISE",
            80,
            100,
            null,
            ShipType: "EXCAVATOR",
            MountSymbols: ["MOUNT_MINING_LASER_I"]);
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miningShip, miningShip with { Symbol = "SHIP-2" }]);

        var planner = new ShipAssignmentPlanner(routes, settings, assignments, waypoints, ships);

        var assignment = await planner.PlanAsync(miningShip, CancellationToken.None);

        assignment.AssignmentType.Should().Be("Mine");
        assignment.OriginWaypoint.Should().Be("X1-AB-AST");
        assignment.DestWaypoint.Should().Be("X1-AB-MKT");
    }
}
