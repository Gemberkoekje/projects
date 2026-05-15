using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps the <c>/contracts</c> read-only API endpoints.</summary>
public static class ContractsReadEndpoints
{
    /// <summary>Registers the contracts read route group on the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapContractsReadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/contracts");

        group.MapGet("/{id}/timeline", async (
            string id,
            IContractRepository contractRepo,
            IActivityLogRepository activityRepo,
            CancellationToken ct) =>
        {
            var contract = await contractRepo.FindAsync(id, ct);
            if (contract is null)
            {
                return Results.NotFound();
            }

            // Return activity log entries related to this contract (best-effort text match on event details).
            var activities = await activityRepo.GetPagedAsync(page: 1, pageSize: 200, cancellationToken: ct);
            var contractActivities = activities
                .Where(a => a.JsonDetails != null && a.JsonDetails.Contains(id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return Results.Ok(new { Contract = contract, Timeline = contractActivities });
        });

        group.MapGet("/{id}/roi", async (
            string id,
            IContractRepository contractRepo,
            ILedgerRepository ledgerRepo,
            CancellationToken ct) =>
        {
            var contract = await contractRepo.FindAsync(id, ct);
            if (contract is null)
            {
                return Results.NotFound();
            }

            var payouts = await ledgerRepo.GetRangeAsync(null, null, null, LedgerCategory.ContractPayout, null, 1000, ct);
            var fuelEntries = await ledgerRepo.GetRangeAsync(null, null, null, LedgerCategory.FuelPurchase, null, 1000, ct);

            var totalPayout = payouts.Sum(e => e.Amount);
            var estimatedFuelCost = Math.Abs(fuelEntries.Sum(e => e.Amount));
            var netRoi = totalPayout - estimatedFuelCost;

            return Results.Ok(new
            {
                ContractId = id,
                TotalPayout = totalPayout,
                EstimatedFuelCost = estimatedFuelCost,
                NetRoi = netRoi,
                IsProfitable = netRoi > 0,
            });
        });

        return app;
    }
}
