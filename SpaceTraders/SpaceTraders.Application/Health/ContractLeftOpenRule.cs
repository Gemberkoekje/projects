using System.Globalization;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Health;

/// <summary>
/// A fulfilled contract has no active plan or assignment (PLAN.md 3.2, B9): once a contract is
/// fulfilled, the contract plan completes and closes its ship's assignment, which releases the ship.
/// The subject is the contract.
/// </summary>
/// <remarks>
/// The plan does that on its next run, within a tick of the fulfilment, so only what stays open for
/// <see cref="Grace"/> counts. While automation or the contract plan is off, nothing closes them, on
/// purpose.
/// </remarks>
public sealed class ContractLeftOpenRule(
    IContractMineralPlanRepository contractPlans,
    IContractRepository contracts,
    IShipAssignmentRepository assignments) : IHealthRule
{
    /// <summary>How long a fulfilled contract's plan or assignment may stay open: a few ticks.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    private const string ContractAssignmentType = "Contract";

    /// <inheritdoc />
    public string Name => "ContractLeftOpen";

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        if (!context.IsOn(AutomationPlan.Contract))
        {
            return [];
        }

        var open = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var plan = await contractPlans.GetAsync(cancellationToken);
        if (plan is { Status: ContractMineralPlanStatus.Active or ContractMineralPlanStatus.PendingBudget }
            && await IsFulfilledAsync(plan.ContractId, cancellationToken))
        {
            Add(open, plan.ContractId, $"the contract plan is still {plan.Status}");
        }

        foreach (var assignment in await assignments.GetAllActiveAsync(cancellationToken))
        {
            if (!assignment.CompletedAt.HasValue
                && assignment.AssignmentType.Equals(ContractAssignmentType, StringComparison.OrdinalIgnoreCase)
                && assignment.ContractId is { Length: > 0 } contractId
                && await IsFulfilledAsync(contractId, cancellationToken))
            {
                Add(open, contractId, $"ship {assignment.ShipSymbol} still has its contract assignment");
            }
        }

        var violations = new List<HealthViolation>();
        foreach (var (contractId, what) in open)
        {
            var since = context.HeldSince($"left-open:{contractId}", condition: true);
            var leftOpen = context.Elapsed(since);
            if (leftOpen > Grace)
            {
                violations.Add(new HealthViolation(
                    contractId,
                    string.Create(CultureInfo.InvariantCulture,
                        $"the contract is fulfilled, but {string.Join(" and ", what)}, for {leftOpen.TotalMinutes:0} minutes; the contract plan closes both on its next run")));
            }
        }

        return violations;
    }

    private static void Add(Dictionary<string, List<string>> open, string contractId, string what)
    {
        if (!open.TryGetValue(contractId, out var list))
        {
            list = [];
            open[contractId] = list;
        }

        list.Add(what);
    }

    private async Task<bool> IsFulfilledAsync(string contractId, CancellationToken cancellationToken)
        => (await contracts.FindAsync(contractId, cancellationToken))?.IsFulfilled == true;
}
