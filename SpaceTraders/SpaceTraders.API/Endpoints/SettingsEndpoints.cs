using SpaceTraders.Application.Queries;
using Wolverine;

namespace SpaceTraders.API.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings");

        group.MapGet("/", async (IMessageBus bus, CancellationToken ct) =>
        {
            var result = await bus.InvokeAsync<IReadOnlyList<Application.DTOs.SettingDto>>(new GetSettingsQuery(), ct);
            return Results.Ok(result);
        });

        group.MapPut("/{key}", async (
            string key,
            UpdateSettingRequest body,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            var settings = sp.GetRequiredService<Application.Interfaces.Repositories.ISettingsRepository>();
            await settings.SetAsync(key, body.Value, ct);
            return Results.Ok();
        });

        group.MapPost("/reset", async (IServiceProvider sp, CancellationToken ct) =>
        {
            var settings = sp.GetRequiredService<Application.Interfaces.Repositories.ISettingsRepository>();
            var all = await settings.GetAllAsync(ct);
            foreach (var s in all)
                await settings.SetAsync(s.Key, s.Value, ct);
            return Results.Ok();
        });

        return app;
    }
}

public record UpdateSettingRequest(string Value);
