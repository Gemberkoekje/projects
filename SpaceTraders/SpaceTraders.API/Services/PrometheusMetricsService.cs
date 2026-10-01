using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.API.Services;

/// <summary>
/// Every 10 seconds, exports the state of the game as the bot has cached it: the agent's credits,
/// every ship (role, state, goal, why its goal is blocked) and the accepted contracts'
/// deliverables. What happens (API calls, goal steps, credits earned and spent) is counted where
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
        var assignmentTypes = assignments
            .GroupBy(a => a.ShipSymbol, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Type, StringComparer.Ordinal);
        ShipMetricsSample[] fleet = [.. ships.Select(ship => ToSample(ship, assignmentTypes, now))];
        metrics.Fleet(fleet, now);
        shipJournal.Observe(fleet);

        var contracts = await db.Contracts.AsNoTracking()
            .Where(c => c.IsAccepted)
            .ToListAsync(cancellationToken);
        metrics.Contracts([.. contracts.SelectMany(ToSamples)]);
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

    private static ShipMetricsSample ToSample(CachedShip ship, Dictionary<string, string> assignmentTypes, DateTimeOffset now)
    {
        var goal = ship.GoalPayloadJson is null ? null : JsonSerializer.Deserialize<ShipGoal>(ship.GoalPayloadJson);
        var goalLabel = ship.GoalKind
            ?? assignmentTypes.GetValueOrDefault(ship.Symbol)
            ?? "None";
        var reason = goal?.Status == GoalStatus.Blocked ? goal.StatusReason ?? "blocked" : string.Empty;

        return new ShipMetricsSample(ship.Symbol, ship.ShipType, State(ship, now), goalLabel, reason);
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
