using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.API.Services;

/// <summary>
/// Every 10 seconds, exports the state of the game as the bot has cached it: the agent's credits,
/// every ship (role, state, goal, why its goal is blocked, where it is, what it does, its hold, what it cost), the
/// accepted contracts' deliverables and the usable surveys. What happens (API calls, goal steps, credits earned and spent) is counted where
/// it happens, through <see cref="IAutomationMetrics"/>. The ship states also feed the journal's
/// <c>ShipIdle</c> lines (<see cref="ShipStateJournal"/>).
/// </summary>
public sealed class PrometheusMetricsService(
    IServiceScopeFactory serviceScopeFactory,
    IAutomationMetrics metrics,
    ShipStateJournal shipJournal,
    ILogger<PrometheusMetricsService> logger) : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions CargoJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the cache once and hands what it found to the metrics.</summary>
    internal async Task SampleAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        var now = TimeProvider.System.GetUtcNow();

        var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (agent is not null)
        {
            metrics.Credits(agent.Credits);
        }

        var ships = await db.Ships.AsNoTracking().ToListAsync(cancellationToken);
        var assignments = await db.ShipAssignments.AsNoTracking()
            .Where(a => a.CompletedAt == null)
            .ToListAsync(cancellationToken);
        var assignmentByShip = assignments
            .GroupBy(a => a.ShipSymbol, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        List<string> places = [.. ships.SelectMany(s => new[] { s.WaypointSymbol, s.DestWaypointSymbol }).OfType<string>().Distinct(StringComparer.Ordinal)];
        var waypointTypes = await db.Waypoints.AsNoTracking()
            .Where(w => places.Contains(w.Symbol))
            .ToDictionaryAsync(w => w.Symbol, w => w.Type, StringComparer.Ordinal, cancellationToken);
        // What was paid for each ship and its equipment. The ledger keeps 30 days, longer than a reset lasts.
        LedgerCategory[] equipment = [LedgerCategory.ShipPurchase, LedgerCategory.MountPurchase, LedgerCategory.ModulePurchase];
        var paid = await db.LedgerEntries.AsNoTracking()
            .Where(e => equipment.Contains(e.Category))
            .GroupBy(e => e.ShipSymbol)
            .Select(g => new { Ship = g.Key, Paid = -g.Sum(e => e.Amount) })
            .ToDictionaryAsync(p => p.Ship, p => p.Paid, StringComparer.Ordinal, cancellationToken);
        ShipMetricsSample[] fleet = [.. ships.Select(ship => ToSample(ship, assignmentByShip, waypointTypes, now) with { Value = paid.GetValueOrDefault(ship.Symbol) })];
        metrics.Fleet(fleet, now);
        shipJournal.Observe(fleet);

        var contracts = await db.Contracts.AsNoTracking()
            .Where(c => c.IsAccepted)
            .ToListAsync(cancellationToken);
        metrics.Contracts([.. contracts.SelectMany(ToSamples)]);

        // Slice 6.4: the usable surveys, used or not yet, for the survey dashboard.
        var surveys = await db.Surveys.AsNoTracking()
            .Where(s => s.Expiration > now)
            .Select(s => new { s.WaypointSymbol, Used = s.Extractions > 0 })
            .ToListAsync(cancellationToken);
        metrics.Surveys([.. surveys
            .GroupBy(s => (s.WaypointSymbol, s.Used))
            .Select(group => new SurveyMetricsSample(group.Key.WaypointSymbol, group.Key.Used, group.Count()))]);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SampleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update Prometheus metrics.");
            }

            await Task.Delay(SampleInterval, stoppingToken);
        }
    }

    private static ShipMetricsSample ToSample(
        CachedShip ship,
        Dictionary<string, ShipAssignmentRecord> assignments,
        Dictionary<string, string> waypointTypes,
        DateTimeOffset now)
    {
        var goal = ship.GoalPayloadJson is null ? null : JsonSerializer.Deserialize<ShipGoal>(ship.GoalPayloadJson);
        var assignment = assignments.GetValueOrDefault(ship.Symbol);
        var goalLabel = ship.GoalKind
            ?? assignment?.Type
            ?? "None";
        var reason = goal?.Status == GoalStatus.Blocked ? goal.StatusReason ?? "blocked" : string.Empty;

        // Cached as in transit, the ship is at (or on its way to) its route's destination.
        var inTransit = ship.ArrivesAt > now;
        var at = string.Equals(ship.Status, "IN_TRANSIT", StringComparison.OrdinalIgnoreCase)
            ? ship.DestWaypointSymbol ?? ship.WaypointSymbol
            : ship.WaypointSymbol;

        return new ShipMetricsSample(ship.Symbol, ship.ShipType, State(ship, now), goalLabel, reason)
        {
            Location = Location(at, inTransit, waypointTypes),
            Activity = Activity(goal, reason, assignment, at, inTransit, IsProbe(ship)),
            ArrivesAt = inTransit ? ship.ArrivesAt.GetValueOrDefault() : default,
            CargoCapacity = ship.CargoCapacity,
            Cargo = Cargo(ship.CargoJson),
        };
    }

    /// <summary>Whether the ship is a probe, as the probe plan counts one (<see cref="FleetRoles.IsProbe"/>).</summary>
    private static bool IsProbe(CachedShip ship)
        => FleetRoles.IsProbe(new ShipModel(ship.Symbol, ship.SystemSymbol, ship.WaypointSymbol, ship.Status, ship.FlightMode, ship.FuelCurrent, ship.FuelCapacity, ShipType: ship.ShipType, FrameJson: ship.FrameJson));

    /// <summary>The waypoint and its type, such as <c>X1-AB-A1 (ASTEROID)</c>; in transit, an arrow first.</summary>
    private static string Location(string? at, bool inTransit, Dictionary<string, string> waypointTypes)
    {
        if (string.IsNullOrEmpty(at))
        {
            return "unknown";
        }

        var place = waypointTypes.TryGetValue(at, out var type) ? $"{at} ({type})" : at;
        return inTransit ? $"→ {place}" : place;
    }

    /// <summary>
    /// What the bot has the ship do, in a few words: a blocked goal, else its goal, else its
    /// assignment, else idle, or for a probe, watching its market. A contract's ship mines at the
    /// contract's source and delivers at its destination.
    /// </summary>
    private static string Activity(ShipGoal? goal, string reason, ShipAssignmentRecord? assignment, string? at, bool inTransit, bool isProbe)
    {
        if (reason.Length > 0)
        {
            return $"blocked ({reason})";
        }

        if (goal is not null)
        {
            return goal switch
            {
                ScoutWaypointGoal => "scouting",
                MineResourceGoal mine => $"mining {mine.TradeSymbol}",
                MineAndSellGoal mineAndSell => mineAndSell.Selling ? $"selling {mineAndSell.TradeSymbol}" : $"mining {mineAndSell.TradeSymbol}",
                SiphonResourceGoal siphon => $"siphoning {siphon.TradeSymbol}",
                SiphonAndSellGoal siphonAndSell => siphonAndSell.Selling ? $"selling {siphonAndSell.TradeSymbol}" : $"siphoning for {siphonAndSell.TradeSymbol}",
                SellCargoGoal => "selling cargo",
                DeliverCargoGoal deliver => $"delivering {deliver.TradeSymbol}",
                SupplyConstructionGoal supply => $"supplying {supply.TradeSymbol} to a construction site",
                TradeBetweenMarketsGoal trade => $"trading {trade.TradeSymbol}",
                SurveyWaypointGoal survey => $"surveying for {survey.TargetDepositSymbol}",
                DeployProbeGoal { ForPurchase: true } => "called to a shipyard",
                DeployProbeGoal => "scouting",
                PatrolMarketGoal => "watching its market",
                MoveToWaypointGoal => "moving",
                IdleGoal => "idle",
                _ => goal.Kind.ToString(),
            };
        }

        if (assignment is null)
        {
            // A probe without a flight stays where it is, and the market watch keeps that market fresh (slice 6.3).
            return isProbe && !inTransit ? "watching its market" : "idle";
        }

        if (!string.Equals(assignment.Type, "Contract", StringComparison.Ordinal))
        {
            return assignment.Type.ToLowerInvariant();
        }

        var good = assignment.CargoSymbol ?? "cargo";
        if (string.Equals(at, assignment.OriginWaypoint, StringComparison.Ordinal))
        {
            return inTransit ? $"on the way to mine {good}" : $"mining {good}";
        }

        if (string.Equals(at, assignment.DestWaypoint, StringComparison.Ordinal))
        {
            return inTransit ? $"on the way to deliver {good}" : $"delivering {good}";
        }

        return $"working on a contract ({good})";
    }

    /// <summary>The hold as cached: startup sync and the repository both store symbol and units.</summary>
    private static IReadOnlyList<CargoItemModel> Cargo(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CargoItemModel>>(json, CargoJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The nav status with arrivals dead-reckoned, as the ship repository applies them.</summary>
    private static string State(CachedShip ship, DateTimeOffset now)
    {
        if (ship.ArrivesAt > now)
        {
            return "IN_TRANSIT";
        }

        return ship.Status?.ToUpperInvariant() switch
        {
            "DOCKED" => "DOCKED",
            "IN_ORBIT" => "IN_ORBIT",
            "IN_TRANSIT" => "IN_ORBIT",
            _ => "UNKNOWN",
        };
    }

    private static IEnumerable<ContractMetricsSample> ToSamples(CachedContract contract)
    {
        var deliverables = string.IsNullOrWhiteSpace(contract.DeliverablesJson)
            ? []
            : JsonSerializer.Deserialize<List<ContractDeliverableDto>>(contract.DeliverablesJson) ?? [];

        return deliverables.Select(d => new ContractMetricsSample(
            contract.Id,
            d.TradeSymbol,
            d.UnitsRequired,
            d.UnitsFulfilled,
            contract.TermsDeadline ?? default));
    }
}
