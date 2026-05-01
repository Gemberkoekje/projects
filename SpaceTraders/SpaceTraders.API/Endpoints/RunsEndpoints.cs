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

        return app;
    }
}
