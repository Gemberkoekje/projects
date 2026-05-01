using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps the <c>/runs</c> read-only API endpoints.</summary>
public static class RunsEndpoints
{
    /// <summary>Registers the runs route group on the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapRunsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/runs");

        group.MapGet("/", async (IRunRepository repo, CancellationToken ct) =>
        {
            var result = await repo.GetAllAsync(ct);
            return Results.Ok(result);
        });

        group.MapGet("/scheduled", async (IRunRepository repo, CancellationToken ct) =>
        {
            var result = await repo.GetScheduledRunsAsync(ct);
            return Results.Ok(result);
        });

        group.MapGet("/compare", async (
            IRunRepository runRepo,
            ILedgerRepository ledgerRepo,
            Guid a,
            Guid b,
            CancellationToken ct) =>
        {
            var runA = await runRepo.GetByIdAsync(a, ct);
            var runB = await runRepo.GetByIdAsync(b, ct);

            if (runA is null || runB is null)
            {
                return Results.NotFound(new { message = "One or both run IDs not found." });
            }

            var highlightsA = await runRepo.GetRunHighlightsAsync(a, ct);
            var highlightsB = await runRepo.GetRunHighlightsAsync(b, ct);
            var summaryA = await ledgerRepo.GetSummaryAsync(a, ct);
            var summaryB = await ledgerRepo.GetSummaryAsync(b, ct);

            return Results.Ok(new
            {
                RunA = new { Run = runA, CreditHighlights = highlightsA, LedgerSummary = summaryA },
                RunB = new { Run = runB, CreditHighlights = highlightsB, LedgerSummary = summaryB },
            });
        });

        group.MapGet("/{id:guid}", async (Guid id, IRunRepository repo, CancellationToken ct) =>
        {
            var result = await repo.GetByIdAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/{id:guid}/summary", async (
            Guid id,
            IRunRepository runRepo,
            ILedgerRepository ledgerRepo,
            CancellationToken ct) =>
        {
            var run = await runRepo.GetByIdAsync(id, ct);
            if (run is null)
            {
                return Results.NotFound();
            }

            var highlights = await runRepo.GetRunHighlightsAsync(id, ct);
            var summary = await ledgerRepo.GetSummaryAsync(id, ct);

            return Results.Ok(new { Run = run, CreditHighlights = highlights, LedgerSummary = summary });
        });

        group.MapGet("/{id:guid}/kpis", async (
            Guid id,
            IRunRepository runRepo,
            ILedgerRepository ledgerRepo,
            IShipTaskRecordRepository taskRepo,
            IApiEndpointUsageRecorder apiUsageRecorder,
            CancellationToken ct) =>
        {
            var run = await runRepo.GetByIdAsync(id, ct);
            if (run is null)
            {
                return Results.NotFound();
            }

            var runEnd = run.EndedAt ?? DateTimeOffset.UtcNow;
            var durationHours = (runEnd - run.StartedAt).TotalHours;
            var deltaCredits = (run.EndingCredits ?? 0L) - run.StartingCredits;

            // credits/hour
            double? creditsPerHour = durationHours > 0 ? deltaCredits / durationHours : null;

            // credits/ship/hour — derive ship count from distinct ships in the ledger
            var shipCount = await ledgerRepo.GetDistinctShipCountAsync(id, ct);
            double? creditsPerShipPerHour = shipCount > 0 && creditsPerHour.HasValue
                ? creditsPerHour.Value / shipCount
                : null;

            // credits/API-call — uses lifetime cumulative total as the best available approximation
            var totalApiCalls = await apiUsageRecorder.GetTotalCallsAsync(ct);
            double? creditsPerApiCall = totalApiCalls > 0 ? (double)deltaCredits / totalApiCalls : null;

            // idle % — computed from ShipTaskRecord rows within the run's time window
            double? idlePercent = null;
            if (shipCount > 0 && durationHours > 0)
            {
                var taskRecords = await taskRepo.GetAllInTimeWindowAsync(run.StartedAt, runEnd, ct);
                var totalShipSeconds = shipCount * (runEnd - run.StartedAt).TotalSeconds;
                var idleSeconds = taskRecords
                    .Where(t => string.Equals(t.TaskKind, "Idle", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => ((t.EndedAt ?? runEnd) - t.StartedAt).TotalSeconds);
                idlePercent = totalShipSeconds > 0 ? idleSeconds / totalShipSeconds * 100.0 : null;
            }

            // fuel cost per credit earned
            var ledgerSummary = await ledgerRepo.GetSummaryAsync(id, ct);
            var totalIncome = ledgerSummary.Where(s => s.TotalAmount > 0).Sum(s => s.TotalAmount);
            var fuelCost = ledgerSummary
                .Where(s => string.Equals(s.Category, "FuelPurchase", StringComparison.Ordinal))
                .Sum(s => Math.Abs(s.TotalAmount));
            double? fuelCostPerCreditEarned = totalIncome > 0 ? (double)fuelCost / totalIncome : null;

            return Results.Ok(new
            {
                CreditsPerHour = creditsPerHour,
                CreditsPerShipPerHour = creditsPerShipPerHour,
                CreditsPerApiCall = creditsPerApiCall,
                IdlePercent = idlePercent,
                FuelCostPerCreditEarned = fuelCostPerCreditEarned,
            });
        });

        return app;
    }
}
