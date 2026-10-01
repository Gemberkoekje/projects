using System.Globalization;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Health;

/// <summary>
/// An accepted contract makes progress (PLAN.md 3.2): units are delivered to the contract the bot
/// works on within <c>Health.Contract.MaxHoursWithoutProgress</c> hours (4) of the previous delivery,
/// or of the plan starting or starting to wait. The subject is the contract.
/// </summary>
/// <remarks>
/// <para>
/// The contract plan writes its state only when something changes, so its <c>UpdatedAt</c> is the
/// last delivery, or when it started (or started waiting). The clock starts no earlier than the
/// monitor saw automation and the contract plan on.
/// </para>
/// <para>
/// A contract parked by decision (D2, not a mineral) is left out; one whose plan found no asteroid
/// is in, because the bot means to mine it. In the soak test (1.14) a drone took 23 to 69 minutes
/// per delivery, on an asteroid that yielded little iron.
/// </para>
/// </remarks>
public sealed class ContractStalledRule(
    IContractMineralPlanRepository contractPlans,
    IContractRepository contracts,
    ISettingsRepository settings) : IHealthRule
{
    /// <summary>The setting that holds the hours a contract may go without a delivery.</summary>
    public const string Setting = "Health.Contract.MaxHoursWithoutProgress";

    /// <summary>The hours a contract may go without a delivery when the setting gives none.</summary>
    public const int DefaultHours = 4;

    /// <inheritdoc />
    public string Name => "ContractStalled";

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var worked = await ContractWork.LoadAsync(contractPlans, contracts, cancellationToken);
        if (worked.Count == 0)
        {
            return [];
        }

        var limit = TimeSpan.FromHours(await settings.ThresholdAsync(Setting, DefaultHours, cancellationToken));
        var violations = new List<HealthViolation>();
        foreach (var contract in worked)
        {
            var since = HealthCheckContext.Latest(contract.Plan.UpdatedAt, context.WorkingSince(AutomationPlan.Contract));
            var stalled = context.Elapsed(since);
            if (stalled > limit)
            {
                violations.Add(new HealthViolation(
                    contract.Contract.Id,
                    string.Create(CultureInfo.InvariantCulture,
                        $"no units delivered for {stalled.TotalHours:0.#} hours, since {since:u}; {contract.Delivered} delivered, contract plan {contract.PlanState}; limit {limit.TotalHours:0} hours ({Setting})")));
            }
        }

        return violations;
    }
}
