using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Services;

public sealed class NavigationPlanningServiceTests
{
    [Fact]
    public async Task BuildPlanAsync_SameSystemLowFuel_PrefersDrift()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var systems = Substitute.For<ISystemRepository>();
        var settings = BuildSettings();

        waypoints.FindAsync("X1-AB-001", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-001", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow));
        waypoints.FindAsync("X1-AB-010", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-010", "X1-AB", "PLANET", 12, 5, true, false, DateTimeOffset.UtcNow));

        var service = new NavigationPlanningService(waypoints, systems, settings);
        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 10, 100);

        var plan = await service.BuildPlanAsync(ship, "X1-AB-010", CancellationToken.None);

        plan.IsCrossSystem.Should().BeFalse();
        plan.RecommendedFlightMode.Should().Be("DRIFT");
        plan.DistanceUnits.Should().BeGreaterThan(0m);
        plan.EstimatedFuelCost.Should().BeGreaterThan(0m);
        plan.EstimatedTravelTimeMinutes.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task BuildPlanAsync_CrossSystemHighFuel_PrefersBurn()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var systems = Substitute.For<ISystemRepository>();
        var settings = BuildSettings();

        waypoints.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((WaypointCacheModel?)null);

        systems.FindAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns(new SystemCacheModel("X1-AB", "X1", "NEUTRON_STAR", 0, 0, DateTimeOffset.UtcNow));
        systems.FindAsync("X1-CD", Arg.Any<CancellationToken>())
            .Returns(new SystemCacheModel("X1-CD", "X1", "NEUTRON_STAR", 80, 40, DateTimeOffset.UtcNow));

        var service = new NavigationPlanningService(waypoints, systems, settings);
        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 90, 100);

        var plan = await service.BuildPlanAsync(ship, "X1-CD-001", CancellationToken.None);

        plan.IsCrossSystem.Should().BeTrue();
        plan.RecommendedFlightMode.Should().Be("BURN");
        plan.DistanceUnits.Should().BeGreaterThan(35m);
    }

    private static ISettingsRepository BuildSettings()
    {
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<decimal>("Navigation.CriticalFuelRatio", Arg.Any<CancellationToken>()).Returns(0.15m);
        settings.GetAsync<decimal>("Navigation.LowFuelRatioForDrift", Arg.Any<CancellationToken>()).Returns(0.35m);
        settings.GetAsync<decimal>("Navigation.BurnFuelRatioMinimum", Arg.Any<CancellationToken>()).Returns(0.50m);
        settings.GetAsync<decimal>("Navigation.BurnDistanceThreshold", Arg.Any<CancellationToken>()).Returns(35m);
        return settings;
    }
}
