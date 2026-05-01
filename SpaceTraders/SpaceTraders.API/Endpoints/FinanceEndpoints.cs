using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps the <c>/finance</c> read-only API endpoints.</summary>
public static class FinanceEndpoints
{
    /// <summary>Registers the finance route group on the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finance");

        group.MapGet("/credits-history", async (
            IAgentCreditsSampleRepository repo,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var effectiveFrom = from ?? DateTimeOffset.UtcNow.AddDays(-7);
            var effectiveTo = to ?? DateTimeOffset.UtcNow;
            var result = await repo.GetRangeAsync(effectiveFrom, effectiveTo, ct);
            return Results.Ok(result);
        });

        group.MapGet("/ledger", async (
            ILedgerRepository repo,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? shipSymbol,
            string? category,
            Guid? runId,
            int limit = 500,
            CancellationToken ct = default) =>
        {
            LedgerCategory? parsedCategory = null;
            if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<LedgerCategory>(category, ignoreCase: true, out var c))
            {
                parsedCategory = c;
            }

            var result = await repo.GetRangeAsync(from, to, shipSymbol, parsedCategory, runId, limit, ct);
            return Results.Ok(result);
        });

        group.MapGet("/summary", async (
            ILedgerRepository repo,
            Guid? runId,
            CancellationToken ct) =>
        {
            var result = await repo.GetSummaryAsync(runId, ct);
            return Results.Ok(result);
        });

        group.MapGet("/run-highlights", async (
            IRunRepository repo,
            Guid runId,
            CancellationToken ct) =>
        {
            var result = await repo.GetRunHighlightsAsync(runId, ct);
            return Results.Ok(result);
        });

        return app;
    }
}
