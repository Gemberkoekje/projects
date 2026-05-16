using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.API.Dtos;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Queries;
using SpaceTraders.Infrastructure.Persistence;
using Wolverine;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps status API endpoints.</summary>
public static class StatusEndpoints
{
    private sealed record JumpGateConnectionSnapshot
    {
        public string WaypointSymbol { get; init; } = string.Empty;

        public IReadOnlyList<string> ConnectedSystems { get; init; } = [];
    }

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

        group.MapGet("/ships/{symbol}/diagnostics", async (string symbol, IMessageBus bus, CancellationToken ct) =>
        {
            var result = await bus.InvokeAsync<Application.DTOs.ShipDiagnosticsDto?>(new GetShipDiagnosticsQuery(symbol), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/waypoints/{symbol}", async (string symbol, IWaypointRepository waypointRepo, CancellationToken ct) =>
        {
            var waypoint = await waypointRepo.FindAsync(symbol, ct);
            return waypoint is null
                ? Results.NotFound()
                : Results.Ok(FleetStatusMapper.ToDto(waypoint));
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

        group.MapGet("/mining-opportunities", async (IPlanRepository plans, CancellationToken ct) =>
        {
            var state = await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, ct);
            state ??= new MiningAutomationPlanState
            {
                PlanId = Guid.Empty,
                Opportunities = [],
                CreatedAt = DateTimeOffset.MinValue,
                UpdatedAt = DateTimeOffset.MinValue,
            };

            return Results.Ok(new
            {
                state.PlanId,
                Opportunities = state.Opportunities.Select(opportunity => new
                {
                    opportunity.OpportunityKey,
                    opportunity.TradeSymbol,
                    opportunity.SellWaypointSymbol,
                    Status = opportunity.Status.ToString(),
                    opportunity.AssignedShipSymbol,
                    opportunity.FirstObservedAt,
                    opportunity.LastObservedAt,
                    opportunity.StopReason,
                }).ToList(),
                state.CreatedAt,
                state.UpdatedAt,
            });
        });

        group.MapGet("/startup-snapshots", async (SpaceTradersDbContext db, CancellationToken ct) =>
        {
            var snapshots = await db.StartupSnapshots
                .AsNoTracking()
                .Where(s => s.AgentToken == db.AgentToken)
                .OrderByDescending(s => s.CapturedAt)
                .Select(s => new
                {
                    s.Id,
                    s.CapturedAt,
                    s.IsInitialSnapshot,
                })
                .ToListAsync(ct);

            return Results.Ok(snapshots);
        });

        group.MapGet("/startup-snapshots/{id:int}/download", async (int id, SpaceTradersDbContext db, CancellationToken ct) =>
        {
            var snapshot = await db.StartupSnapshots
                .AsNoTracking()
                .Where(s => s.AgentToken == db.AgentToken && s.Id == id)
                .Select(s => new
                {
                    s.SnapshotJson,
                    s.CapturedAt,
                    s.IsInitialSnapshot,
                })
                .SingleOrDefaultAsync(ct);

            if (snapshot is null)
            {
                return Results.NotFound();
            }

            var capturedAt = snapshot.CapturedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss");
            var initialSuffix = snapshot.IsInitialSnapshot ? "-initial" : string.Empty;
            var fileName = $"startup-snapshot-{id}-{capturedAt}{initialSuffix}.json";

            return Results.File(Encoding.UTF8.GetBytes(snapshot.SnapshotJson), "application/json", fileName);
        });

        group.MapGet("/system-alerts", async (ISettingsRepository settings, CancellationToken ct) =>
        {
            var automationEnabled = await settings.GetAsync<bool>("Automation.Enabled", ct);
            var response = new
            {
                ApiUnavailable = await settings.GetAsync<bool>("Runtime.Alert.ApiUnavailable", ct),
                TokenResetMismatch = await settings.GetAsync<bool>("Runtime.Alert.TokenResetMismatch", ct),
                CacheDivergence = await settings.GetAsync<bool>("Runtime.Alert.CacheDivergence", ct),
                AutomationDisabled = !automationEnabled,
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
            var sampleOneHourAgo = samples.Where(s => s.ObservedAt >= now.AddHours(-1)).OrderBy(s => s.ObservedAt).FirstOrDefault();

            double avgCreditRatePerHour = 0;
            double recentCreditRatePerHour = 0;

            if (sample24hAgo is not null && sampleNow is not null)
            {
                avgCreditRatePerHour = (sampleNow.Credits - sample24hAgo.Credits) / 24.0;
            }

            if (sampleOneHourAgo is not null && sampleNow is not null)
            {
                var hoursElapsed = (sampleNow.ObservedAt - sampleOneHourAgo.ObservedAt).TotalHours;
                if (hoursElapsed > 0)
                    recentCreditRatePerHour = (sampleNow.Credits - sampleOneHourAgo.Credits) / hoursElapsed;
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

        var universe = app.MapGroup("/universe");

        universe.MapGet("/systems", async (
            ISystemRepository systems,
            IWaypointRepository waypoints,
            CancellationToken ct) =>
        {
            var allSystems = await systems.GetAllAsync(ct);
            var visitedSet = (await waypoints.GetVisitedSystemSymbolsAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var result = allSystems
                .Select(system => new
                {
                    system.Symbol,
                    system.SectorSymbol,
                    system.Type,
                    system.X,
                    system.Y,
                    system.LastObservedAt,
                    IsVisited = visitedSet.Contains(system.Symbol),
                })
                .ToList();

            return Results.Ok(result);
        });

        universe.MapGet("/jump-connections", async (ISettingsRepository settings, CancellationToken ct) =>
        {
            var rows = await settings.GetByKeyPrefixAsync("Navigation.JumpGateConnections.", ct);
            var connections = new List<(string FromSystem, string ToSystem)>();

            foreach (var (_, json) in rows)
            {
                JumpGateConnectionSnapshot snapshot;
                try
                {
                    snapshot = JsonSerializer.Deserialize<JumpGateConnectionSnapshot>(json) ?? new JumpGateConnectionSnapshot();
                }
                catch (JsonException)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(snapshot.WaypointSymbol))
                {
                    continue;
                }

                var segments = snapshot.WaypointSymbol.Split('-', StringSplitOptions.RemoveEmptyEntries);
                var fromSystem = segments.Length >= 2
                    ? $"{segments[0]}-{segments[1]}"
                    : snapshot.WaypointSymbol;

                foreach (var toSystem in snapshot.ConnectedSystems)
                {
                    if (string.IsNullOrWhiteSpace(toSystem))
                    {
                        continue;
                    }

                    connections.Add((fromSystem, toSystem));
                }
            }

            var result = connections
                .Distinct()
                .Select(connection => new
                {
                    connection.FromSystem,
                    connection.ToSystem,
                })
                .ToList();

            return Results.Ok(result);
        });

        var runs = app.MapGroup("/runs");

        runs.MapGet("/{runId:guid}/kpis", async (
            Guid runId,
            IRunRepository runRepository,
            CancellationToken ct) =>
        {
            var run = await runRepository.GetByIdAsync(runId, ct);
            return run is null ? Results.NotFound() : Results.Ok(new Application.DTOs.RunKpisDto());
        });

        var finance = app.MapGroup("/finance");

        finance.MapGet("/trade-routes", async (ITradeOpportunityRepository repo, CancellationToken ct) =>
        {
            var routes = await repo.GetTopRoutesAsync(10, ct);
            var result = routes
                .Select(route => new
                {
                    Good = route.TradeSymbol,
                    route.BuyWaypoint,
                    route.SellWaypoint,
                    TotalUnits = route.EffectiveTradeVolume,
                    TotalProfit = route.ProfitPerUnit * route.EffectiveTradeVolume,
                    RunDurationHours = 0d,
                    ProfitPerHour = 0d,
                })
                .ToList();

            return Results.Ok(result);
        });

        return app;
    }
}
