using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Orchestration;

/// <summary>
/// Decides whether a proposed expense fits within the agent's credits
/// while preserving the configured credit reserve.
/// </summary>
/// <remarks>
/// Reads the reserve from the <c>FleetExpansion.MinCreditReserve</c> setting.
/// </remarks>
public interface IBudgetPolicy
{
    Task<BudgetDecision> EvaluateAsync(long proposedCost, CancellationToken cancellationToken = default);
}

public sealed class BudgetPolicy(
    IAgentRepository agents,
    ISettingsRepository settings) : IBudgetPolicy
{
    private const string MinCreditReserveSetting = "FleetExpansion.MinCreditReserve";
    private const string FabMatsBuyThresholdSetting = "Construction.FabMatsBuyThreshold";
    private const string FabMatsTransactionSizeSetting = "Construction.FabMatsTransactionSize";
    private const string ConstructionHourlyBudgetCapEnabledSetting = "Construction.HourlyBudgetCapEnabled";

    public async Task<BudgetDecision> EvaluateAsync(long proposedCost, CancellationToken cancellationToken = default)
    {
        var agent = await agents.GetAsync(cancellationToken);
        var available = agent?.Credits ?? 0;
        var reserved = await settings.GetAsync<long>(MinCreditReserveSetting, cancellationToken);
        var fabMatsBuyThreshold = await settings.GetAsync<int>(FabMatsBuyThresholdSetting, cancellationToken);
        var fabMatsTransactionSize = await settings.GetAsync<int>(FabMatsTransactionSizeSetting, cancellationToken);
        var hourlyConstructionBudgetCapEnabled = await settings.GetAsync<bool>(ConstructionHourlyBudgetCapEnabledSetting, cancellationToken);
        if (reserved < 0)
        {
            reserved = 0;
        }

        if (fabMatsBuyThreshold <= 0)
        {
            fabMatsBuyThreshold = 2_500;
        }

        if (fabMatsTransactionSize <= 0)
        {
            fabMatsTransactionSize = 60;
        }

        var spendable = available - reserved;
        if (spendable < 0)
        {
            spendable = 0;
        }

        if (proposedCost <= 0)
        {
            return new BudgetDecision(true, available, reserved, spendable,
                FabMatsBuyThreshold: fabMatsBuyThreshold,
                FabMatsTransactionSize: fabMatsTransactionSize,
                HourlyConstructionBudgetCapEnabled: hourlyConstructionBudgetCapEnabled);
        }

        if (proposedCost > spendable)
        {
            return new BudgetDecision(
                CanAfford: false,
                AvailableCredits: available,
                ReservedCredits: reserved,
                SpendableCredits: spendable,
                Reason: $"Proposed cost {proposedCost} exceeds spendable credits {spendable} (reserve {reserved}).",
                FabMatsBuyThreshold: fabMatsBuyThreshold,
                FabMatsTransactionSize: fabMatsTransactionSize,
                HourlyConstructionBudgetCapEnabled: hourlyConstructionBudgetCapEnabled);
        }

        return new BudgetDecision(true, available, reserved, spendable,
            FabMatsBuyThreshold: fabMatsBuyThreshold,
            FabMatsTransactionSize: fabMatsTransactionSize,
            HourlyConstructionBudgetCapEnabled: hourlyConstructionBudgetCapEnabled);
    }
}
