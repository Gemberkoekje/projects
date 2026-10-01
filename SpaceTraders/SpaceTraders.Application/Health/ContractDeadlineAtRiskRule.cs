using System.Globalization;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Health;

/// <summary>
/// A deadline within N hours comes with at least X% delivered (PLAN.md 3.2): from
/// <c>Health.Contract.DeadlineHours</c> (24) before its deadline, the contract the bot works on has at
/// least <c>Health.Contract.MinDeliveredPercent</c> (50) of its units delivered. The subject is the
/// contract.
/// </summary>
/// <remarks>
/// A contract that missed its deadline is an anomaly whatever was delivered, for as long as the plan
/// still holds it: it failed. While automation or the contract plan is off, nothing is expected of the
/// contract. A contract parked by decision (D2, not a mineral) is left out.
/// </remarks>
public sealed class ContractDeadlineAtRiskRule(
    IContractMineralPlanRepository contractPlans,
    IContractRepository contracts,
    ISettingsRepository settings) : IHealthRule
{
    /// <summary>The setting that holds how many hours before the deadline the rule starts to look.</summary>
    public const string HoursSetting = "Health.Contract.DeadlineHours";

    /// <summary>The setting that holds the share of the units that must be delivered by then, in percent.</summary>
    public const string PercentSetting = "Health.Contract.MinDeliveredPercent";

    /// <summary>The hours before the deadline when the setting gives none.</summary>
    public const int DefaultHours = 24;

    /// <summary>The percentage delivered when the setting gives none.</summary>
    public const int DefaultPercent = 50;

    /// <inheritdoc />
    public string Name => "ContractDeadlineAtRisk";

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        if (!context.IsOn(AutomationPlan.Contract))
        {
            return [];
        }

        var worked = await ContractWork.LoadAsync(contractPlans, contracts, cancellationToken);
        if (worked.Count == 0)
        {
            return [];
        }

        var hours = await settings.ThresholdAsync(HoursSetting, DefaultHours, cancellationToken);
        var percent = await settings.ThresholdAsync(PercentSetting, DefaultPercent, cancellationToken);
        var violations = new List<HealthViolation>();
        foreach (var contract in worked)
        {
            if (contract.Contract.TermsDeadline is not { } deadline || deadline - context.Now > TimeSpan.FromHours(hours))
            {
                continue;
            }

            var delivered = contract.UnitsRequired == 0 ? 100 : contract.UnitsFulfilled * 100 / contract.UnitsRequired;
            var left = deadline - context.Now;
            if (left <= TimeSpan.Zero)
            {
                violations.Add(new HealthViolation(
                    contract.Contract.Id,
                    string.Create(CultureInfo.InvariantCulture,
                        $"it missed its deadline {left.Negate().TotalHours:0.#} hours ago ({deadline:u}) with {contract.Delivered} delivered ({delivered}%)")));
            }
            else if (delivered < percent)
            {
                violations.Add(new HealthViolation(
                    contract.Contract.Id,
                    string.Create(CultureInfo.InvariantCulture,
                        $"its deadline is in {left.TotalHours:0.#} hours ({deadline:u}) with {contract.Delivered} delivered ({delivered}%); from {hours} hours before the deadline at least {percent}% should be ({HoursSetting}, {PercentSetting})")));
            }
        }

        return violations;
    }
}
