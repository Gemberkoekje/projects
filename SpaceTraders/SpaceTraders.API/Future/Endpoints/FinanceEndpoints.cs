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

        group.MapGet("/trade-routes", async (
            ILedgerRepository repo,
            Guid? runId,
            CancellationToken ct) =>
        {
            var from = runId is null ? DateTimeOffset.UtcNow.AddDays(-7) : (DateTimeOffset?)null;
            var to = runId is null ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;

            var buyEntries = await repo.GetRangeAsync(from, to, null, LedgerCategory.TradeBuy, runId, 5000, ct);
            var sellEntries = await repo.GetRangeAsync(from, to, null, LedgerCategory.TradeSell, runId, 5000, ct);

            // Pair buys with sells per (ship, good) in chronological order
            var routes = new List<(string Good, string BuyWp, string SellWp, long Profit, int Units)>();

            var buysByShipGood = buyEntries
                .Where(e => e.GoodSymbol != null && e.WaypointSymbol != null)
                .GroupBy(e => (e.ShipSymbol, e.GoodSymbol!))
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.OccurredAt).ToList());

            var sellsByShipGood = sellEntries
                .Where(e => e.GoodSymbol != null && e.WaypointSymbol != null)
                .GroupBy(e => (e.ShipSymbol, e.GoodSymbol!))
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.OccurredAt).ToList());

            foreach (var (key, buys) in buysByShipGood)
            {
                if (!sellsByShipGood.TryGetValue(key, out var sells)) continue;
                var sellIdx = 0;
                foreach (var buy in buys)
                {
                    while (sellIdx < sells.Count && sells[sellIdx].OccurredAt <= buy.OccurredAt)
                        sellIdx++;
                    if (sellIdx >= sells.Count) break;
                    var sell = sells[sellIdx];
                    var units = Math.Min(buy.Units ?? 1, sell.Units ?? 1);
                    var profit = sell.Amount + buy.Amount;
                    routes.Add((key.Item2, buy.WaypointSymbol!, sell.WaypointSymbol!, profit, units));
                }
            }

            // Compute run duration for profit/hour
            var allTimes = sellEntries.Select(e => e.OccurredAt).ToList();
            var runDurationHours = allTimes.Count >= 2
                ? Math.Max(1.0, (allTimes.Max() - allTimes.Min()).TotalHours)
                : (runId is null ? 7.0 * 24.0 : 24.0);

            var aggregated = routes
                .GroupBy(r => (r.Good, r.BuyWp, r.SellWp))
                .Select(g => new
                {
                    Good = g.Key.Good,
                    BuyWaypoint = g.Key.BuyWp,
                    SellWaypoint = g.Key.SellWp,
                    TotalUnits = g.Sum(r => r.Units),
                    TotalProfit = g.Sum(r => r.Profit),
                    RunDurationHours = runDurationHours,
                    ProfitPerHour = runDurationHours > 0 ? g.Sum(r => r.Profit) / runDurationHours : 0,
                })
                .OrderByDescending(r => r.ProfitPerHour)
                .ToList();

            return Results.Ok(aggregated);
        });

        return app;
    }
}
