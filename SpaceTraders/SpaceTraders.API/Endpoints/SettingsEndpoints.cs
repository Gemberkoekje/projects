using SpaceTraders.Application.Queries;
using SpaceTraders.API.Services;
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
            var snapshotLogger = sp.GetRequiredService<SettingsSnapshotLogger>();
            await settings.SetAsync(key, body.Value, ct);
            await snapshotLogger.LogAsync($"settings updated: {key}", ct);
            return Results.Ok();
        });

        group.MapPost("/reset", async (IServiceProvider sp, CancellationToken ct) =>
        {
            var settings = sp.GetRequiredService<Application.Interfaces.Repositories.ISettingsRepository>();
            var snapshotLogger = sp.GetRequiredService<SettingsSnapshotLogger>();
            await settings.ResetToDefaultsAsync(ct);
            await snapshotLogger.LogAsync("settings reset", ct);
            return Results.Ok();
        });

        return app;
    }
}

public sealed record UpdateSettingRequest
{
    public required string Value { get; init; }
}
