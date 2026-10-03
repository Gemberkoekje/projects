using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.API.Services;

/// <summary>
/// Every 10 seconds, exports the state of the game as the bot has cached it: the agent's credits,
/// every ship (role, state, goal, why its goal is blocked, where it is, what it does, what it can do, its hold, what it cost), the
/// accepted contracts' deliverables and the usable surveys; the bot's settings; and what each plan would buy, in the order ships
/// are bought in (slice 6.10b, from <see cref="PurchaseNeeds"/>). What happens (API calls, goal steps, credits earned and spent) is counted where
/// it happens, through <see cref="IAutomationMetrics"/>. The ship states also feed the journal's
/// <c>ShipIdle</c> lines (<see cref="ShipStateJournal"/>).
/// </summary>
public sealed class PrometheusMetricsService(
    IServiceScopeFactory serviceScopeFactory,
    IAutomationMetrics metrics,
    ShipStateJournal shipJournal,
    PurchaseNeeds purchaseNeeds,
    FullHoldSavings fullHoldSavings,
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

        // Slice 2.9: the settings, for the dashboard's settings table. A secret stays hidden, as in SettingChanged; what a
        // setting does is what this version says, as a stored description is the one it was seeded with.
        var settings = await db.Settings.AsNoTracking().ToListAsync(cancellationToken);
        metrics.Settings([.. settings.Select(setting => new SettingMetricsSample(
            setting.Key,
            SettingsRepository.Shown(setting.Key, setting.Value),
            DefaultSettingsSeed.DescriptionOf(setting.Key) ?? setting.Description))]);

        // Slice 6.9: the role board, for the dashboard's roles table, while it is on: switched off, the plans no longer
        // read the roles its state keeps.
        var boardOn = IsOn(settings.Find(setting => setting.Key == AutomationSwitches.PlanEnabledSetting(AutomationPlan.Roles))?.Value);
        var roles = boardOn
            ? await db.PlanStates.AsNoTracking()
                .Where(plan => plan.PlanType == PlanTypes.Roles)
                .Select(plan => plan.StateJson)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var roleSamples = RoleSamples(roles);
        metrics.Roles(roleSamples);

        // D51: what a ship purchase must leave, by what the ships that trade can carry, next to the credits; the dearest full
        // hold a trader saves up for (D56); and what the trade trips on their way to buy hold back (D57).
        var roleOf = roleSamples.ToDictionary(
            sample => sample.Ship,
            sample => Enum.TryParse<FleetRole>(sample.Role, out var role) ? role : FleetRole.None,
            StringComparer.Ordinal);
        var floor = settings.Find(setting => setting.Key == CreditReserve.FloorSetting)?.Value;
        metrics.ReservedCredits(CreditReserve.Of(
            long.TryParse(floor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var floorCredits) ? floorCredits : 0,
            CreditReserve.PerTradingCargoUnit(settings.Find(setting => setting.Key == CreditReserve.PerTradingCargoUnitSetting)?.Value ?? string.Empty),
            CreditReserve.TradingCargo(ships.Select(ShipRepository.MapToModel), ship => roleOf.GetValueOrDefault(ship.Symbol, FleetRole.None)))
            + fullHoldSavings.Largest()
            + TripHolds(ships));

        // Slice 6.10b (D43): what the credits are saved up for, and what waits behind it.
        metrics.PurchaseNeeds([.. purchaseNeeds.Open(now).Select(open => new PurchaseNeedMetricsSample(
            open.Plan.ToString(),
            open.Need.Tier.ToString(),
            (int)open.Need.Tier,
            open.Need.ShipType,
            open.Need.ShipyardWaypointSymbol,
            open.Need.Price))]);
    }

    /// <summary>The role board's ships, from its state's JSON; none without a readable state.</summary>
    internal static IReadOnlyCollection<RoleMetricsSample> RoleSamples(string? stateJson)
    {
        RolePlanState? state;
        try
        {
            state = string.IsNullOrWhiteSpace(stateJson) ? null : JsonSerializer.Deserialize<RolePlanState>(stateJson);
        }
        catch (JsonException)
        {
            state = null;
        }

        return [.. (state?.Ships ?? []).Select(ship => new RoleMetricsSample(
            ship.ShipSymbol,
            ship.Role.ToString(),
            ship.Reason,
            ship.Estimates.ToDictionary(estimate => estimate.Role.ToString(), estimate => estimate.CreditsPerHour, StringComparer.Ordinal)))];
    }

    /// <summary>Whether a switch's stored value is on, read as the settings repository reads a <c>bool</c>: JSON <c>true</c>.</summary>
    internal static bool IsOn(string? value)
    {
        try
        {
            return value is not null && JsonSerializer.Deserialize<bool>(value);
        }
        catch (JsonException)
        {
            return false;
        }
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

    /// <summary>
    /// What the trade trips on their way to buy hold back for their cargo (D57), from the goals cached with the ships, with
    /// the stored status as the goal store reads it.
    /// </summary>
    private static long TripHolds(IEnumerable<CachedShip> ships)
    {
        var trips = new List<KeyValuePair<string, TradeBetweenMarketsGoal>>();
        foreach (var ship in ships.Where(ship => ship.GoalKind == nameof(ShipGoalKind.TradeBetweenMarkets)))
        {
            if (ship.GoalPayloadJson is { } json && JsonSerializer.Deserialize<ShipGoal>(json) is TradeBetweenMarketsGoal trip)
            {
                trips.Add(new(ship.Symbol, ship.GoalStatus is { } status ? trip with { Status = (GoalStatus)status } : trip));
            }
        }

        return TripReservations.HeldBack(trips);
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

        // The ship as the plans see it: its mounts, frame, hold and tank decide what it is and what it can do.
        var model = ShipRepository.MapToModel(ship);

        return new ShipMetricsSample(ship.Symbol, ship.ShipType, State(ship, now), goalLabel, reason)
        {
            Location = Location(at, inTransit, waypointTypes),
            Activity = Activity(goal, reason, assignment, at, inTransit, FleetRoles.IsProbe(model)),
            Capabilities = Capabilities(model),
            ArrivesAt = inTransit ? ship.ArrivesAt.GetValueOrDefault() : default,
            CargoCapacity = ship.CargoCapacity,
            Cargo = Cargo(ship.CargoJson),
        };
    }

    /// <summary>
    /// What the ship's equipment lets it do, whichever plans are on: the roles it could take
    /// (<see cref="FleetRoles.PotentialRoles"/>), in the order survey, mine, siphon, trade, such as <c>Siphon, Trade</c>;
    /// <c>none</c> for a probe or a ship that can do none of them.
    /// </summary>
    private static string Capabilities(ShipModel ship)
    {
        var roles = FleetRoles.PotentialRoles(ship);
        return roles.Count == 0 ? "none" : string.Join(", ", roles);
    }

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

                // A trip to a market out of the ship's CRUISE reach drifts there first, for hours (slice 6.10c, D45).
                MineAndSellGoal { Drifting: true } far => $"drifting to {far.SellWaypointSymbol} to mine {far.TradeSymbol}",
                MineAndSellGoal mineAndSell => mineAndSell.Selling ? $"selling {mineAndSell.TradeSymbol}" : $"mining {mineAndSell.TradeSymbol}",
                SiphonResourceGoal siphon => $"siphoning {siphon.TradeSymbol}",
                SiphonAndSellGoal { Drifting: true } far => $"drifting to {far.SellWaypointSymbol} to siphon for {far.TradeSymbol}",
                SiphonAndSellGoal siphonAndSell => siphonAndSell.Selling ? $"selling {siphonAndSell.TradeSymbol}" : $"siphoning for {siphonAndSell.TradeSymbol}",
                GatherAndSellGoal { Selling: true } gather => gather.SellTradeSymbol.Length > 0 ? $"selling {gather.SellTradeSymbol}" : "selling its hold",
                GatherAndSellGoal gather => gather.Siphoning ? "siphoning in its spare time" : "mining in its spare time",
                SellCargoGoal => "selling cargo",
                DeliverCargoGoal deliver => $"delivering {deliver.TradeSymbol}",
                SupplyConstructionGoal supply => $"supplying {supply.TradeSymbol} to a construction site",
                TradeBetweenMarketsGoal trade => $"trading {trade.TradeSymbol}",
                SurveyWaypointGoal survey => $"surveying for {survey.TargetDepositSymbol}",
                DeployProbeGoal { ForPurchase: true } => "called to a shipyard",
                DeployProbeGoal => "scouting",
                PatrolMarketGoal => "watching its market",
                MoveToWaypointGoal { Drifting: true } move => $"drifting to {move.TargetWaypointSymbol}",
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
