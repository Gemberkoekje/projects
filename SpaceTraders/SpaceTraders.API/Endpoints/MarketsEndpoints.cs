using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps the <c>/markets</c> read-only API endpoints.</summary>
public static class MarketsEndpoints
{
    /// <summary>Registers the markets route group on the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapMarketsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/markets");

        group.MapGet("/goods/{symbol}/prices", async (
            string symbol,
            IMarketPriceSampleRepository repo,
            string? waypoint,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var effectiveFrom = from ?? DateTimeOffset.UtcNow.AddDays(-7);
            var effectiveTo = to ?? DateTimeOffset.UtcNow;
            var result = await repo.GetGoodPricesAsync(symbol, waypoint, effectiveFrom, effectiveTo, ct);
            return Results.Ok(result);
        });

        group.MapGet("/waypoints/{symbol}/prices", async (
            string symbol,
            IMarketPriceSampleRepository repo,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var effectiveFrom = from ?? DateTimeOffset.UtcNow.AddDays(-7);
            var effectiveTo = to ?? DateTimeOffset.UtcNow;
            var result = await repo.GetWaypointPricesAsync(symbol, effectiveFrom, effectiveTo, ct);
            return Results.Ok(result);
        });

        group.MapGet("/best-routes", async (
            ITradeOpportunityRepository repo,
            ISettingsRepository settings,
            int cargoCapacity = int.MaxValue,
            int maxJumps = 5,
            CancellationToken ct = default) =>
        {
            var minProfit = await settings.GetAsync<int>("Trade.MinProfitPerUnit", ct);
            var result = await repo.GetBestRouteForCapacityAsync(cargoCapacity, minProfit, maxJumps, ct);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });

        return app;
    }
}
