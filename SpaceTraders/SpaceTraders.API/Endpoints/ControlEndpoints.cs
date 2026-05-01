using SpaceTraders.API.Services;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
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

        group.MapPost("/ships/{symbol}/dock", async (string symbol, IMessageBus bus) =>
        {
            await bus.SendAsync(new DockShipCommand(symbol));
            return Results.Accepted();
        });

        group.MapPost("/ships/{symbol}/orbit", async (string symbol, IMessageBus bus) =>
        {
            await bus.SendAsync(new OrbitShipCommand(symbol));
            return Results.Accepted();
        });

        group.MapPost("/ships/{symbol}/refuel", async (string symbol, RefuelRequest body, IMessageBus bus) =>
        {
            await bus.SendAsync(new RefuelShipCommand(symbol, body.FromCargo));
            return Results.Accepted();
        });

        group.MapPost("/ships/{symbol}/navigate", async (string symbol, NavigateRequest body, IMessageBus bus) =>
        {
            await bus.SendAsync(new NavigateShipCommand(symbol, body.DestinationWaypoint));
            return Results.Accepted();
        });

        group.MapPost("/ships/{symbol}/flight-mode", async (string symbol, FlightModeRequest body, IMessageBus bus) =>
        {
            await bus.SendAsync(new PatchShipNavCommand(symbol, body.FlightMode));
            return Results.Accepted();
        });

        group.MapPost("/ships/{symbol}/repair", async (string symbol, IMessageBus bus) =>
        {
            await bus.SendAsync(new RepairShipCommand(symbol));
            return Results.Accepted();
        });

        group.MapPost("/ships/{symbol}/scrap", async (string symbol, IMessageBus bus) =>
        {
            await bus.SendAsync(new ScrapShipCommand(symbol));
            return Results.Accepted();
        });

        group.MapPost("/ships/{symbol}/mounts/install", async (string symbol, InstallOrRemoveEquipmentRequest body, IMessageBus bus) =>
        {
            await bus.SendAsync(new InstallMountCommand(symbol, body.Symbol));
            return Results.Accepted();
        });

        group.MapPost("/ships/{symbol}/mounts/remove", async (string symbol, InstallOrRemoveEquipmentRequest body, IMessageBus bus) =>
        {
            await bus.SendAsync(new RemoveMountCommand(symbol, body.Symbol));
            return Results.Accepted();
        });

        group.MapPost("/ships/{symbol}/modules/install", async (string symbol, InstallOrRemoveEquipmentRequest body, IMessageBus bus) =>
        {
            await bus.SendAsync(new InstallModuleCommand(symbol, body.Symbol));
            return Results.Accepted();
        });

        group.MapPost("/ships/{symbol}/modules/remove", async (string symbol, InstallOrRemoveEquipmentRequest body, IMessageBus bus) =>
        {
            await bus.SendAsync(new RemoveModuleCommand(symbol, body.Symbol));
            return Results.Accepted();
        });

        group.MapPost("/sync", async (IMessageBus bus) =>
        {
            await bus.SendAsync(new SyncAllShipsCommand());
            await bus.SendAsync(new SyncAgentCommand());
            await bus.SendAsync(new SyncContractsCommand());
            return Results.Accepted();
        });

        group.MapPost("/runs/schedule", async (
            ScheduleRunRequest body,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            var runRepo = sp.GetRequiredService<IRunRepository>();
            var id = await runRepo.ScheduleRunAsync(
                body.Name,
                body.StrategyLabel,
                body.ScheduledSettingsJson,
                body.ActivatesAt,
                body.ActivatesOnNextRestart,
                ct);
            return Results.Created($"/control/runs/schedule/{id}", new { id });
        });

        group.MapDelete("/runs/schedule/{id:guid}", async (
            Guid id,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            var runRepo = sp.GetRequiredService<IRunRepository>();
            var deleted = await runRepo.DeleteScheduledRunAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }
}

public sealed record ReassignRequest
{
    public required string AssignmentType { get; init; }
}

public sealed record RefuelRequest
{
    public bool FromCargo { get; init; }
}

public sealed record NavigateRequest
{
    public required string DestinationWaypoint { get; init; }
}

public sealed record FlightModeRequest
{
    public required string FlightMode { get; init; }
}

public sealed record InstallOrRemoveEquipmentRequest
{
    public required string Symbol { get; init; }
}

public sealed record ScheduleRunRequest
{
    public required string Name { get; init; }

    public required string StrategyLabel { get; init; }

    public string? ScheduledSettingsJson { get; init; }

    public DateTimeOffset? ActivatesAt { get; init; }

    public bool ActivatesOnNextRestart { get; init; }
}
