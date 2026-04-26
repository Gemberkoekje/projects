using SpaceTraders.API.Services;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Sync;
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
            await snapshotLogger.LogAsync("control automation enabled", ct);
            return Results.Ok(new { enabled = true });
        });

        group.MapPost("/automation/disable", async (IServiceProvider sp, CancellationToken ct) =>
        {
            var settings = sp.GetRequiredService<Application.Interfaces.Repositories.ISettingsRepository>();
            var snapshotLogger = sp.GetRequiredService<SettingsSnapshotLogger>();
            await settings.SetAsync("Automation.Enabled", "false", ct);
            await snapshotLogger.LogAsync("control automation disabled", ct);
            return Results.Ok(new { enabled = false });
        });

        group.MapPost("/ships/{symbol}/reassign", async (
            string symbol,
            ReassignRequest body,
            IMessageBus bus) =>
        {
            await bus.SendAsync(new AssignShipCommand(symbol, body.AssignmentType));
            return Results.Accepted();
        });

        group.MapPost("/sync", async (IMessageBus bus) =>
        {
            await bus.SendAsync(new SyncAllShipsCommand());
            await bus.SendAsync(new SyncAgentCommand());
            await bus.SendAsync(new SyncContractsCommand());
            return Results.Accepted();
        });

        return app;
    }
}

public sealed record ReassignRequest
{
    public required string AssignmentType { get; init; }
}
