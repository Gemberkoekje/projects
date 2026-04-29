using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

public interface INavigationPlanningService
{
    Task<NavigationPlan> BuildPlanAsync(ShipModel ship, string destinationWaypoint, CancellationToken cancellationToken);
}

public sealed record NavigationPlan
{
    public required decimal DistanceUnits { get; init; }

    public required decimal EstimatedFuelCost { get; init; }

    public required decimal EstimatedTravelTimeMinutes { get; init; }

    public required string RecommendedFlightMode { get; init; }

    public required bool IsCrossSystem { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NavigationPlan(
        decimal distanceUnits,
        decimal estimatedFuelCost,
        decimal estimatedTravelTimeMinutes,
        string recommendedFlightMode,
        bool isCrossSystem)
    {
        DistanceUnits = distanceUnits;
        EstimatedFuelCost = estimatedFuelCost;
        EstimatedTravelTimeMinutes = estimatedTravelTimeMinutes;
        RecommendedFlightMode = recommendedFlightMode;
        IsCrossSystem = isCrossSystem;
    }
}

public sealed class NavigationPlanningService(
    IWaypointRepository waypoints,
    ISystemRepository systems,
    ISettingsRepository settings) : INavigationPlanningService
{
    public async Task<NavigationPlan> BuildPlanAsync(ShipModel ship, string destinationWaypoint, CancellationToken cancellationToken)
    {
        var destinationSystem = ExtractSystemSymbol(destinationWaypoint);
        var sourceSystem = ship.SystemSymbol ?? destinationSystem;
        var isCrossSystem = !string.Equals(sourceSystem, destinationSystem, StringComparison.OrdinalIgnoreCase);

        var distance = await ResolveDistanceAsync(ship, destinationWaypoint, sourceSystem, destinationSystem, cancellationToken);
        var fuelRatio = ship.FuelCapacity > 0 ? (decimal)ship.FuelCurrent / ship.FuelCapacity : 0m;

        var recommendedMode = await ResolveFlightModeAsync(distance, fuelRatio, cancellationToken);
        var estimatedFuelCost = EstimateFuelCost(distance, recommendedMode);
        var estimatedTravelMinutes = EstimateTravelTimeMinutes(distance, recommendedMode);

        return new NavigationPlan(
            distance,
            estimatedFuelCost,
            estimatedTravelMinutes,
            recommendedMode,
            isCrossSystem);
    }

    private async Task<decimal> ResolveDistanceAsync(
        ShipModel ship,
        string destinationWaypoint,
        string sourceSystem,
        string destinationSystem,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(ship.WaypointSymbol) &&
            string.Equals(sourceSystem, destinationSystem, StringComparison.OrdinalIgnoreCase))
        {
            var source = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
            var destination = await waypoints.FindAsync(destinationWaypoint, cancellationToken);
            if (source is not null && destination is not null)
            {
                return CalculateDistance(source.X, source.Y, destination.X, destination.Y);
            }
        }

        var sourceSystemData = await systems.FindAsync(sourceSystem, cancellationToken);
        var destinationSystemData = await systems.FindAsync(destinationSystem, cancellationToken);
        if (sourceSystemData is not null && destinationSystemData is not null)
        {
            return CalculateDistance(sourceSystemData.X, sourceSystemData.Y, destinationSystemData.X, destinationSystemData.Y);
        }

        return 12m;
    }

    private async Task<string> ResolveFlightModeAsync(decimal distance, decimal fuelRatio, CancellationToken cancellationToken)
    {
        var criticalFuelRatio = await settings.GetAsync<decimal>("Navigation.CriticalFuelRatio", cancellationToken);
        var lowFuelRatioForDrift = await settings.GetAsync<decimal>("Navigation.LowFuelRatioForDrift", cancellationToken);
        var burnFuelRatioMinimum = await settings.GetAsync<decimal>("Navigation.BurnFuelRatioMinimum", cancellationToken);
        var burnDistanceThreshold = await settings.GetAsync<decimal>("Navigation.BurnDistanceThreshold", cancellationToken);

        var critical = criticalFuelRatio > 0m ? criticalFuelRatio : 0.15m;
        var lowFuel = lowFuelRatioForDrift > 0m ? lowFuelRatioForDrift : 0.35m;
        var burnFuel = burnFuelRatioMinimum > 0m ? burnFuelRatioMinimum : 0.50m;
        var burnDistance = burnDistanceThreshold > 0m ? burnDistanceThreshold : 35m;

        if (fuelRatio <= critical)
        {
            return "DRIFT";
        }

        if (distance >= burnDistance && fuelRatio >= burnFuel)
        {
            return "BURN";
        }

        if (fuelRatio <= lowFuel)
        {
            return "DRIFT";
        }

        return "CRUISE";
    }

    private static decimal EstimateFuelCost(decimal distance, string flightMode)
    {
        var normalizedDistance = Math.Max(1m, distance);
        var baseFuel = Math.Max(1m, decimal.Ceiling(normalizedDistance / 8m));

        if (flightMode.Equals("BURN", StringComparison.OrdinalIgnoreCase)) return baseFuel * 2m;
        if (flightMode.Equals("DRIFT", StringComparison.OrdinalIgnoreCase)) return Math.Max(1m, baseFuel * 0.5m);
        if (flightMode.Equals("STEALTH", StringComparison.OrdinalIgnoreCase)) return baseFuel * 1.1m;
        return baseFuel;
    }

    private static decimal EstimateTravelTimeMinutes(decimal distance, string flightMode)
    {
        var normalizedDistance = Math.Max(1m, distance);
        var baseMinutes = Math.Max(1m, decimal.Ceiling(normalizedDistance / 2m));

        if (flightMode.Equals("BURN", StringComparison.OrdinalIgnoreCase)) return Math.Max(1m, baseMinutes / 1.5m);
        if (flightMode.Equals("DRIFT", StringComparison.OrdinalIgnoreCase)) return baseMinutes * 2.5m;
        if (flightMode.Equals("STEALTH", StringComparison.OrdinalIgnoreCase)) return baseMinutes * 1.3m;
        return baseMinutes;
    }

    private static decimal CalculateDistance(int x1, int y1, int x2, int y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var value = Math.Sqrt((dx * dx) + (dy * dy));
        return (decimal)value;
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}
