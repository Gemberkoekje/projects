using System.Globalization;
using System.Text.Json;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Health;

/// <summary>The contract the bot works on, as the contract rules see it.</summary>
public sealed record WorkedContract
{
    /// <summary>Creates the view of the worked contract.</summary>
    /// <param name="Plan">The contract plan.</param>
    /// <param name="Contract">The cached contract.</param>
    /// <param name="Deliverables">The contract's deliverables.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public WorkedContract(ContractMineralPlanState Plan, ContractDto Contract, IReadOnlyList<ContractDeliverableDto> Deliverables)
    {
        this.Plan = Plan;
        this.Contract = Contract;
        this.Deliverables = Deliverables;
    }

    /// <summary>The contract plan.</summary>
    public required ContractMineralPlanState Plan { get; init; }

    /// <summary>The cached contract, which every delivery updates.</summary>
    public required ContractDto Contract { get; init; }

    /// <summary>The contract's deliverables.</summary>
    public required IReadOnlyList<ContractDeliverableDto> Deliverables { get; init; }

    /// <summary>Units the contract requires, over all its deliverables.</summary>
    public int UnitsRequired => Deliverables.Sum(deliverable => deliverable.UnitsRequired);

    /// <summary>Units delivered so far, over all its deliverables.</summary>
    public int UnitsFulfilled => Deliverables.Sum(deliverable => deliverable.UnitsFulfilled);

    /// <summary>What has been delivered, for the journal: <c>11/42 IRON_ORE</c>.</summary>
    public string Delivered => string.Create(CultureInfo.InvariantCulture,
        $"{UnitsFulfilled}/{UnitsRequired} {string.Join(", ", Deliverables.Select(deliverable => deliverable.TradeSymbol).Distinct(StringComparer.Ordinal))}");

    /// <summary>
    /// What the contract plan is doing, for the journal: <c>Active</c>, or its status and why it
    /// stopped (<c>PendingBudget: No idle mining ship available ...</c>).
    /// </summary>
    public string PlanState => string.IsNullOrWhiteSpace(Plan.StopReason)
        ? Plan.Status.ToString()
        : $"{Plan.Status}: {Plan.StopReason.TrimEnd('.')}";
}

/// <summary>Finds the contract the bot works on.</summary>
internal static class ContractWork
{
    /// <summary>
    /// The contract plan's contract, while the plan works on it (Active), waits for a ship or budget
    /// (PendingBudget), or found no asteroid for its mineral (DeferredUnsupported); and only while the
    /// contract is accepted and not fulfilled. A contract whose deliverable isn't a mineral is parked by
    /// decision (D2), so nobody works on it.
    /// </summary>
    /// <param name="contractPlans">The contract plan's state.</param>
    /// <param name="contracts">The cached contracts.</param>
    /// <param name="cancellationToken">Stops the load.</param>
    /// <returns>The worked contract, or none.</returns>
    public static async Task<IReadOnlyList<WorkedContract>> LoadAsync(
        IContractMineralPlanRepository contractPlans,
        IContractRepository contracts,
        CancellationToken cancellationToken)
    {
        var plan = await contractPlans.GetAsync(cancellationToken);
        if (plan is null || !IsWorkedOn(plan))
        {
            return [];
        }

        var contract = await contracts.FindAsync(plan.ContractId, cancellationToken);
        if (contract is null || !contract.IsAccepted || contract.IsFulfilled)
        {
            return [];
        }

        var deliverables = string.IsNullOrWhiteSpace(contract.DeliverablesJson)
            ? []
            : JsonSerializer.Deserialize<List<ContractDeliverableDto>>(contract.DeliverablesJson) ?? [];

        return [new WorkedContract(plan, contract, deliverables)];
    }

    private static bool IsWorkedOn(ContractMineralPlanState plan) => plan.Status switch
    {
        ContractMineralPlanStatus.Active or ContractMineralPlanStatus.PendingBudget => true,
        ContractMineralPlanStatus.DeferredUnsupported => ContractPlanService.IsMineralSymbol(plan.TradeSymbol),
        _ => false,
    };
}
