using SpaceTraders.Application.Queries;
using Wolverine;

namespace SpaceTraders.API.Endpoints;

public static class StatusEndpoints
{
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

        return app;
    }
}
