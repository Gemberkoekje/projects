using System.Text.Json;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Orchestration;

/// <summary>
/// Produces strategic goals from world state. Each evaluator focuses on a single
/// concern: contracts, construction, market coverage, or fleet expansion.
/// </summary>
public interface IFleetGoalEvaluator
{
    Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default);
}

public sealed class ContractGoalEvaluator(IContractRepository contracts) : IFleetGoalEvaluator
{
    public async Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var active = await contracts.GetActiveAsync(cancellationToken);
        var goals = new List<FleetGoal>();
        foreach (var contract in active)
        {
            if (!contract.IsAccepted || contract.IsFulfilled)
            {
                continue;
            }

            var deliverables = DeserializeDeliverables(contract.DeliverablesJson);
            foreach (var deliverable in deliverables)
            {
                var remaining = deliverable.UnitsRequired - deliverable.UnitsFulfilled;
                if (remaining <= 0)
                {
                    continue;
                }

                goals.Add(new FleetGoal(
                    Kind: FleetGoalKind.Contract,
                    Description: $"Deliver {remaining} {deliverable.TradeSymbol} to {deliverable.DestinationSymbol} for contract {contract.Id}.",
                    Priority: 100,
                    ContractId: contract.Id,
                    TradeSymbol: deliverable.TradeSymbol,
                    RemainingUnits: remaining,
                    Deadline: contract.TermsDeadline ?? contract.Expiration));
            }
        }

        return goals;
    }

    private static IReadOnlyList<ContractDeliverableDto> DeserializeDeliverables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ContractDeliverableDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public sealed class ConstructionGoalEvaluator(IConstructionRepository constructions) : IFleetGoalEvaluator
{
    public async Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var sites = await constructions.GetIncompleteAsync(cancellationToken);
        var goals = new List<FleetGoal>();
        foreach (var site in sites)
        {
            foreach (var material in site.Materials)
            {
                var remaining = material.Required - material.Fulfilled;
                if (remaining <= 0)
                {
                    continue;
                }

                goals.Add(new FleetGoal(
                    Kind: FleetGoalKind.Construction,
                    Description: $"Supply {remaining} {material.TradeSymbol} to construction site {site.WaypointSymbol}.",
                    Priority: 80,
                    TradeSymbol: material.TradeSymbol,
                    DestinationWaypoint: site.WaypointSymbol,
                    RemainingUnits: remaining));
            }
        }

        return goals;
    }
}

public sealed class MarketCoverageGoalEvaluator(
    IShipRepository ships,
    IWaypointRepository waypoints,
    IShipAssignmentRepository assignments) : IFleetGoalEvaluator
{
    private const string MarketProbeAssignmentType = "MarketProbe";

    public async Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var fleet = await ships.GetAllAsync(cancellationToken);
        if (fleet.Count == 0)
        {
            return [];
        }

        var systems = fleet
            .Select(s => s.SystemSymbol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (systems.Count == 0)
        {
            return [];
        }

        var knownMarkets = new List<string>();
        foreach (var system in systems)
        {
            var systemWaypoints = await waypoints.GetBySystemAsync(system!, cancellationToken);
            foreach (var w in systemWaypoints.Where(w => w.HasMarket))
            {
                knownMarkets.Add(w.Symbol);
            }
        }

        if (knownMarkets.Count == 0)
        {
            return [];
        }

        var activeAssignments = await assignments.GetAllActiveAsync(cancellationToken);
        var coveredMarkets = activeAssignments
            .Where(a =>
                a.AssignmentType.Equals(MarketProbeAssignmentType, StringComparison.OrdinalIgnoreCase)
                && !a.CompletedAt.HasValue
                && !string.IsNullOrWhiteSpace(a.OriginWaypoint))
            .Select(a => a.OriginWaypoint!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var goals = new List<FleetGoal>();
        foreach (var market in knownMarkets)
        {
            if (coveredMarkets.Contains(market))
            {
                continue;
            }

            goals.Add(new FleetGoal(
                Kind: FleetGoalKind.MarketCoverage,
                Description: $"Place market probe at {market}.",
                Priority: 40,
                OriginWaypoint: market));
        }

        return goals;
    }
}

public sealed class FleetExpansionGoalEvaluator(
    IFleetCapacityEstimator capacity,
    IBudgetPolicy budget,
    ISettingsRepository settings,
    IAgentRepository agents) : IFleetGoalEvaluator
{
    public async Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var agent = await agents.GetAsync(cancellationToken);
        if (agent is null)
        {
            return [];
        }

        var maxShips = await settings.GetAsync<int>("FleetExpansion.MaxShips", cancellationToken);
        if (maxShips > 0 && agent.ShipCount >= maxShips)
        {
            return [];
        }

        var estimate = await capacity.EstimateAsync(cancellationToken);
        if (estimate.IdleShips > 0)
        {
            // Reassign idle ships before recommending expansion.
            return [];
        }

        var decision = await budget.EvaluateAsync(0, cancellationToken);
        if (!decision.CanAfford || decision.SpendableCredits <= 0)
        {
            return [];
        }

        return [new FleetGoal(
            Kind: FleetGoalKind.FleetExpansion,
            Description: "Expand fleet: no idle ships, spendable credits available.",
            Priority: 30,
            EstimatedCost: decision.SpendableCredits)];
    }
}
