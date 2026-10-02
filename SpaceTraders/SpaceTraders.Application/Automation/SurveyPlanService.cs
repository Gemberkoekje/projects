using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The survey plan (PLAN.md slice 6.4).</summary>
public interface ISurveyPlanService
{
    /// <summary>
    /// One pass of the plan: ends the surveys that expired, and gives every free surveyor its next
    /// survey.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The survey plan (PLAN.md slice 6.4). Every ship that can survey surveys, and does nothing else (D20):
/// each tick a free surveyor gets one survey to take (<see cref="SurveyWaypointGoal"/>), chosen by
/// <see cref="MiningPlanner.SurveyTargets"/>:
/// <list type="bullet">
///   <item>the contract's ore, at the contract's asteroid, while the contract plan mines it;</item>
///   <item>otherwise an ore a market in the system buys, at the asteroid nearest the market that pays most
///   for it, among those the miners can reach; ores without a usable survey first, then the best paid.</item>
/// </list>
/// A surveyor takes the best target no other surveyor works on, or the best one when all are taken. The
/// plan also ends the surveys that expired (<see cref="ISurveyKeeper.ExpireAsync"/>), for the survey
/// dashboard.
/// </summary>
public sealed class SurveyPlanService(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipAssignmentRepository assignments,
    IContractMineralPlanRepository contractPlans,
    IMiningContextReader miningContexts,
    ISurveyKeeper surveyKeeper,
    IPlanRepository plans,
    ILogger<SurveyPlanService> logger) : ISurveyPlanService
{
    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();
        await surveyKeeper.ExpireAsync(now, cancellationToken);

        var fleet = await ships.GetAllAsync(cancellationToken);
        var withAssignment = (await assignments.GetAllActiveAsync(cancellationToken))
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var surveying = new Dictionary<string, SurveyWaypointGoal>(StringComparer.OrdinalIgnoreCase);
        var free = new List<ShipModel>();
        foreach (var ship in fleet.Where(ship => FleetRoles.IsSurveyor(ship, surveyPlanOn: true)))
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (goal is SurveyWaypointGoal survey && survey.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                surveying[ship.Symbol] = survey;
            }
            else if (FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                free.Add(ship);
            }
        }

        var contract = await contractPlans.GetAsync(cancellationToken);
        var targets = new List<SurveyPlanTarget>();
        foreach (var system in fleet
            .Where(ship => FleetRoles.IsSurveyor(ship, surveyPlanOn: true) && !string.IsNullOrWhiteSpace(ship.SystemSymbol))
            .GroupBy(ship => ship.SystemSymbol!, StringComparer.OrdinalIgnoreCase))
        {
            var context = await miningContexts.ReadAsync(system.Key, cancellationToken);
            var miners = fleet
                .Where(ship => FleetRoles.IsMiner(ship, surveyPlanOn: true)
                    && string.Equals(ship.SystemSymbol, system.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var systemTargets = MiningPlanner.SurveyTargets(context, ContractOres(contract, system.Key), miners);

            foreach (var surveyor in free.Where(ship => string.Equals(ship.SystemSymbol, system.Key, StringComparison.OrdinalIgnoreCase)))
            {
                var reachable = systemTargets.Where(target => MiningPlanner.CanReach(context.Map, surveyor, target.AsteroidSymbol)).ToList();
                if (reachable.Count == 0)
                {
                    logger.LogDebug("Survey plan: nothing to survey that ship {ShipSymbol} can reach.", surveyor.Symbol);
                    continue;
                }

                var target = reachable.FirstOrDefault(candidate => !surveying.Values.Any(goal => Targets(goal, candidate))) ?? reachable[0];
                var goal = new SurveyWaypointGoal { TargetWaypointSymbol = target.AsteroidSymbol, TargetDepositSymbol = target.Ore };
                await goals.SetActiveGoalAsync(surveyor.Symbol, goal, cancellationToken);
                surveying[surveyor.Symbol] = goal;
                logger.LogDebug(
                    "Survey plan: ship {ShipSymbol} surveys {WaypointSymbol} for {TradeSymbol}{ForContract}.",
                    surveyor.Symbol,
                    target.AsteroidSymbol,
                    target.Ore,
                    target.ForContract ? " (the contract's)" : string.Empty);
            }

            targets.AddRange(systemTargets.Select(target => new SurveyPlanTarget
            {
                TradeSymbol = target.Ore,
                WaypointSymbol = target.AsteroidSymbol,
                BuyerWaypointSymbol = target.BuyerSymbol,
                ForContract = target.ForContract,
                HasUsableSurvey = target.HasUsableSurvey,
                SurveyorShipSymbols = [.. surveying.Where(entry => Targets(entry.Value, target)).Select(entry => entry.Key).Order(StringComparer.Ordinal)],
            }));
        }

        await SaveStateAsync(targets, now, cancellationToken);
    }

    /// <summary>The contract's ore, while the contract plan mines it in this system.</summary>
    private static IReadOnlyList<ContractOre> ContractOres(ContractMineralPlanState? contract, string systemSymbol)
        => contract is { Status: ContractMineralPlanStatus.Active }
            && contract.UnitsFulfilled < contract.UnitsRequired
            && !string.IsNullOrWhiteSpace(contract.SourceWaypoint)
            && contract.SourceWaypoint.StartsWith(systemSymbol + "-", StringComparison.OrdinalIgnoreCase)
                ? [new ContractOre(contract.TradeSymbol, contract.SourceWaypoint, contract.DestinationWaypoint)]
                : [];

    private static bool Targets(SurveyWaypointGoal goal, SurveyTarget target)
        => goal.TargetWaypointSymbol.Equals(target.AsteroidSymbol, StringComparison.OrdinalIgnoreCase)
            && goal.TargetDepositSymbol.Equals(target.Ore, StringComparison.OrdinalIgnoreCase);

    /// <summary>Records the targets. Only a change is written: the tick runs every 5 seconds.</summary>
    private async Task SaveStateAsync(IReadOnlyList<SurveyPlanTarget> targets, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await plans.GetAsync<SurveyPlanState>(PlanTypes.Survey, cancellationToken);
        if (existing is not null
            && JsonSerializer.Serialize(existing.Targets, CompareOptions) == JsonSerializer.Serialize(targets, CompareOptions))
        {
            return;
        }

        await plans.UpsertAsync(
            PlanTypes.Survey,
            new SurveyPlanState
            {
                PlanId = existing?.PlanId ?? Guid.NewGuid(),
                Targets = targets,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now,
            },
            cancellationToken);
    }
}
