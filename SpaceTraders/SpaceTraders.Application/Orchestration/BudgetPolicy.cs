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

    public async Task<BudgetDecision> EvaluateAsync(long proposedCost, CancellationToken cancellationToken = default)
    {
        var agent = await agents.GetAsync(cancellationToken);
        var available = agent?.Credits ?? 0;
        var reserved = await settings.GetAsync<long>(MinCreditReserveSetting, cancellationToken);
        if (reserved < 0)
        {
            reserved = 0;
        }

        var spendable = available - reserved;
        if (spendable < 0)
        {
            spendable = 0;
        }

        if (proposedCost <= 0)
        {
            return new BudgetDecision(true, available, reserved, spendable);
        }

        if (proposedCost > spendable)
        {
            return new BudgetDecision(
                CanAfford: false,
                AvailableCredits: available,
                ReservedCredits: reserved,
                SpendableCredits: spendable,
                Reason: $"Proposed cost {proposedCost} exceeds spendable credits {spendable} (reserve {reserved}).");
        }

        return new BudgetDecision(true, available, reserved, spendable);
    }
}
