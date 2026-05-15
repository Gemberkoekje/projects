using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps additional read-only endpoints for ships.</summary>
public static class ShipsReadEndpoints
{
    /// <summary>Registers the ships read route group on the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapShipsReadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ships");

        group.MapGet("/{symbol}/timeline", async (
            string symbol,
            IShipTaskRecordRepository taskRepo,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var effectiveFrom = from ?? DateTimeOffset.UtcNow.AddDays(-7);
            var effectiveTo = to ?? DateTimeOffset.UtcNow;
            var result = await taskRepo.GetTimelineAsync(symbol, effectiveFrom, effectiveTo, ct);
            return Results.Ok(result);
        });

        group.MapGet("/{symbol}/stats", async (
            string symbol,
            ILedgerRepository ledgerRepo,
            IShipRepository shipRepo,
            Guid? runId,
            CancellationToken ct) =>
        {
            var ship = await shipRepo.FindAsync(symbol, ct);
            if (ship is null)
            {
                return Results.NotFound();
            }

            var ledger = await ledgerRepo.GetRangeAsync(
                shipSymbol: symbol,
                runId: runId,
                limit: 1000,
                cancellationToken: ct);

            var summary = await ledgerRepo.GetSummaryAsync(runId, ct);

            return Results.Ok(new { Ship = ship, Ledger = ledger, Summary = summary });
        });

        return app;
    }
}
