using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

public interface IFleetMaintenancePlanner
{
    Task<FleetMaintenanceDecision> DecideAsync(ShipModel ship, string assignmentType, CancellationToken cancellationToken);
}

public sealed record FleetMaintenanceDecision
{
    public required bool ShouldRepair { get; init; }

    public required bool ShouldScrap { get; init; }

    public required bool AvoidLongRoutes { get; init; }

    public string? Reason { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public FleetMaintenanceDecision(bool ShouldRepair, bool ShouldScrap, bool AvoidLongRoutes, string? Reason = null)
    {
        this.ShouldRepair = ShouldRepair;
        this.ShouldScrap = ShouldScrap;
        this.AvoidLongRoutes = AvoidLongRoutes;
        this.Reason = Reason;
    }
}

public sealed class FleetMaintenancePlanner(
    ISettingsRepository settings,
    ISpaceTradersPort port) : IFleetMaintenancePlanner
{
    public async Task<FleetMaintenanceDecision> DecideAsync(ShipModel ship, string assignmentType, CancellationToken cancellationToken)
    {
        var repairThreshold = await settings.GetAsync<decimal>("Maintenance.RepairConditionThreshold", cancellationToken);
        if (repairThreshold <= 0m)
        {
            repairThreshold = 0.70m;
        }

        var minIntegrityForLongRoutes = await settings.GetAsync<decimal>("Maintenance.MinIntegrityForLongRoutes", cancellationToken);
        if (minIntegrityForLongRoutes <= 0m)
        {
            minIntegrityForLongRoutes = 0.55m;
        }

        var scrapThreshold = await settings.GetAsync<decimal>("Maintenance.ScrapIntegrityThreshold", cancellationToken);
        if (scrapThreshold <= 0m)
        {
            scrapThreshold = 0.20m;
        }

        var minScrapValue = await settings.GetAsync<long>("Maintenance.MinScrapValue", cancellationToken);
        if (minScrapValue <= 0)
        {
            minScrapValue = 5000;
        }

        var shouldRepair = ship.AverageCondition < repairThreshold;
        var avoidLongRoutes = ship.AverageIntegrity < minIntegrityForLongRoutes;

        var shouldScrap = false;
        if (ship.AverageIntegrity <= scrapThreshold)
        {
            var scrapQuote = await port.GetScrapQuoteAsync(ship.Symbol, cancellationToken);
            shouldScrap = scrapQuote.Value >= minScrapValue;
        }

        var reason = shouldScrap
            ? $"Integrity {ship.AverageIntegrity:F2} is below scrap threshold {scrapThreshold:F2}."
            : shouldRepair
                ? $"Condition {ship.AverageCondition:F2} is below repair threshold {repairThreshold:F2}."
                : avoidLongRoutes
                    ? $"Integrity {ship.AverageIntegrity:F2} is below long-route threshold {minIntegrityForLongRoutes:F2}."
                    : null;

        return new FleetMaintenanceDecision(shouldRepair, shouldScrap, avoidLongRoutes, reason);
    }
}
