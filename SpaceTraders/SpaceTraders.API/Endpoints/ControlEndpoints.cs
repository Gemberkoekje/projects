using SpaceTraders.API.Services;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using Wolverine;

namespace SpaceTraders.API.Endpoints;

public static class ControlEndpoints
{
    public static IEndpointRouteBuilder MapControlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/control");

        group.MapPost("/automation/enable", async (IServiceProvider sp, CancellationToken ct) =>
        {
            var settings = sp.GetRequiredService<Application.Interfaces.Repositories.ISettingsRepository>();
            var snapshotLogger = sp.GetRequiredService<SettingsSnapshotLogger>();
            await settings.SetAsync("Automation.Enabled", "true", ct);
            await settings.SetAsync("Runtime.AutomationPausedByReset", "false", ct);
            await settings.SetAsync("Runtime.Alert.AutomationDisabled", "false", ct);
            await snapshotLogger.LogAsync("control automation enabled", ct);
            return Results.Ok(new { enabled = true });
        });

        group.MapPost("/automation/disable", async (IServiceProvider sp, CancellationToken ct) =>
        {
            var settings = sp.GetRequiredService<Application.Interfaces.Repositories.ISettingsRepository>();
            var snapshotLogger = sp.GetRequiredService<SettingsSnapshotLogger>();
            await settings.SetAsync("Automation.Enabled", "false", ct);
            await settings.SetAsync("Runtime.AutomationPausedByReset", "false", ct);
            await settings.SetAsync("Runtime.Alert.AutomationDisabled", "true", ct);
            await snapshotLogger.LogAsync("control automation disabled", ct);
            return Results.Ok(new { enabled = false });
        });



        return app;
    }
}

public sealed record RefuelRequest
{
    public bool FromCargo { get; init; }
}

public sealed record NavigateRequest
{
    public required string DestinationWaypoint { get; init; }
}
