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

        return app;
    }
}
