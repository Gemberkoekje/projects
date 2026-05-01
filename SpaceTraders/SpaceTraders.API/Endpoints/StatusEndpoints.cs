using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Queries;
using Wolverine;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps status API endpoints.</summary>
public static class StatusEndpoints
{
    /// <summary>Registers the status route group on the given <paramref name="app"/>.</summary>
    /// <param name="app">The endpoint route builder to register the status routes on.</param>
    /// <returns>The updated endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/status");

        group.MapGet("/agent", async (IMessageBus bus, CancellationToken ct) =>
        {
            var result = await bus.InvokeAsync<Application.DTOs.AgentDto?>(new GetAgentQuery(), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/ships", async (IMessageBus bus, CancellationToken ct) =>
        {
            var result = await bus.InvokeAsync<IReadOnlyList<Application.DTOs.ShipDto>>(new GetAllShipsQuery(), ct);
            return Results.Ok(result);
        });

        group.MapGet("/contracts", async (IMessageBus bus, CancellationToken ct) =>
        {
            var result = await bus.InvokeAsync<IReadOnlyList<Application.DTOs.ContractDto>>(new GetActiveContractsQuery(), ct);
            return Results.Ok(result);
        });

        group.MapGet("/rate-limit", async (IMessageBus bus, CancellationToken ct) =>
        {
            var result = await bus.InvokeAsync<Application.DTOs.RateLimitStatusDto>(new GetRateLimitStatusQuery(), ct);
            return Results.Ok(result);
        });

        group.MapGet("/activity", async (
            IMessageBus bus,
            int page = 1,
            int size = 50,
            string? ship = null,
            CancellationToken ct = default) =>
        {
            var result = await bus.InvokeAsync<IReadOnlyList<Application.DTOs.ActivityLogDto>>(new GetActivityLogQuery(page, size, ship), ct);
            return Results.Ok(result);
        });

        group.MapGet("/trade-opportunities", async (IMessageBus bus, CancellationToken ct) =>
        {
            var result = await bus.InvokeAsync<Application.DTOs.TradeOpportunityDto?>(new GetBestTradeRouteQuery(int.MaxValue), ct);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });

        group.MapGet("/system-alerts", async (ISettingsRepository settings, CancellationToken ct) =>
        {
            var response = new
            {
                ApiUnavailable = await settings.GetAsync<bool>("Runtime.Alert.ApiUnavailable", ct),
                TokenResetMismatch = await settings.GetAsync<bool>("Runtime.Alert.TokenResetMismatch", ct),
                CacheDivergence = await settings.GetAsync<bool>("Runtime.Alert.CacheDivergence", ct),
                AutomationDisabled = await settings.GetAsync<bool>("Runtime.Alert.AutomationDisabled", ct),
                ContractDeadlinesApproaching = await settings.GetAsync<bool>("Runtime.Alert.ContractDeadlinesApproaching", ct),
                ResetUpcoming = await settings.GetAsync<bool>("Runtime.Alert.ResetUpcoming", ct),
                NextReset = await settings.GetRawAsync("Runtime.Reset.Next", ct),
            };

            return Results.Ok(response);
        });

        group.MapGet("/anomalies", async (
            IAgentCreditsSampleRepository creditRepo,
            CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var samples = await creditRepo.GetRangeAsync(now.AddHours(-25), now, ct);

            var sample24hAgo = samples.Where(s => s.ObservedAt >= now.AddHours(-25) && s.ObservedAt <= now.AddHours(-24)).OrderBy(s => s.ObservedAt).LastOrDefault();
            var sampleNow = samples.OrderBy(s => s.ObservedAt).LastOrDefault();
            var sampleOnehAgo = samples.Where(s => s.ObservedAt >= now.AddHours(-1)).OrderBy(s => s.ObservedAt).FirstOrDefault();

            double avgCreditRatePerHour = 0;
            double recentCreditRatePerHour = 0;

            if (sample24hAgo is not null && sampleNow is not null)
            {
                avgCreditRatePerHour = (sampleNow.Credits - sample24hAgo.Credits) / 24.0;
            }

            if (sampleOnehAgo is not null && sampleNow is not null)
            {
                var hoursElapsed = (sampleNow.ObservedAt - sampleOnehAgo.ObservedAt).TotalHours;
                if (hoursElapsed > 0)
                    recentCreditRatePerHour = (sampleNow.Credits - sampleOnehAgo.Credits) / hoursElapsed;
            }

            var creditGrowthAnomaly = avgCreditRatePerHour > 1000
                && recentCreditRatePerHour < 0.5 * avgCreditRatePerHour;

            return Results.Ok(new
            {
                CreditGrowthAnomaly = creditGrowthAnomaly,
                RecentCreditRatePerHour = recentCreditRatePerHour,
                AvgCreditRatePerHour = avgCreditRatePerHour,
                PriceAnomalies = Array.Empty<object>(),
            });
        });

        group.MapGet("/top-trade-routes", async (
            ITradeOpportunityRepository repo,
            int limit = 10,
            CancellationToken ct = default) =>
        {
            var result = await repo.GetTopRoutesAsync(limit, ct);
            return Results.Ok(result);
        });

        return app;
    }
}
