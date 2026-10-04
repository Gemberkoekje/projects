using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Orchestration;

/// <summary>
/// Decides whether a proposed expense fits within the agent's credits
/// while preserving the configured credit reserve.
/// </summary>
/// <remarks>
/// The reserve grows with what the ships that trade can carry (D51, <see cref="CreditReserve"/>):
/// <c>FleetExpansion.MinCreditReserve</c> and <c>FleetExpansion.ReservePerTradingCargoUnit</c> a unit, judged from the
/// cached fleet and the role board on every evaluation; while a trader saves up for a full hold the credits don't pay
/// for yet, by the dearest such hold (D56, <see cref="FullHoldSavings"/>), so ships are bought after it; and by what the
/// trade and construction trips on their way to buy hold back for their cargo (D57, D59, <see cref="TripReservations"/>).
/// A load of materials for the jump gate is judged like a ship purchase (slice 6.6, D59): it pays nothing back.
/// </remarks>
public interface IBudgetPolicy
{
    Task<BudgetDecision> EvaluateAsync(long proposedCost, CancellationToken cancellationToken = default);
}

public sealed class BudgetPolicy(
    IAgentRepository agents,
    ISettingsRepository settings,
    IShipRepository ships,
    IPlanRepository plans,
    FullHoldSavings savings,
    IShipGoalRepository goals) : IBudgetPolicy
{
    private const string FabMatsBuyThresholdSetting = "Construction.FabMatsBuyThreshold";
    private const string FabMatsTransactionSizeSetting = "Construction.FabMatsTransactionSize";
    private const string ConstructionHourlyBudgetCapEnabledSetting = "Construction.HourlyBudgetCapEnabled";

    public async Task<BudgetDecision> EvaluateAsync(long proposedCost, CancellationToken cancellationToken = default)
    {
        var agent = await agents.GetAsync(cancellationToken);
        var available = agent?.Credits ?? 0;
        var reserved = await ReserveAsync(cancellationToken);
        var fabMatsBuyThreshold = await settings.GetAsync<int>(FabMatsBuyThresholdSetting, cancellationToken);
        var fabMatsTransactionSize = await settings.GetAsync<int>(FabMatsTransactionSizeSetting, cancellationToken);
        var hourlyConstructionBudgetCapEnabled = await settings.GetAsync<bool>(ConstructionHourlyBudgetCapEnabledSetting, cancellationToken);

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

    /// <summary>
    /// The credit reserve now (D51): the floor, and the credits per unit the ships that trade can carry; the dearest full
    /// hold a trader saves up for (D56); and what the trade and construction trips on their way to buy hold back (D57, D59).
    /// </summary>
    private async Task<long> ReserveAsync(CancellationToken cancellationToken)
    {
        var floor = await settings.GetAsync<long>(CreditReserve.FloorSetting, cancellationToken);
        var perUnit = CreditReserve.PerTradingCargoUnit(await settings.GetRawAsync(CreditReserve.PerTradingCargoUnitSetting, cancellationToken) ?? string.Empty);
        var board = await FleetRoleBoard.ReadAsync(settings, plans, cancellationToken);
        var fleet = await ships.GetAllAsync(cancellationToken);
        return CreditReserve.Of(floor, perUnit, CreditReserve.TradingCargo(fleet, board.RoleOf))
            + savings.Largest()
            + TripReservations.HeldBack(await goals.GetActiveTradeGoalsAsync(cancellationToken))
            + TripReservations.HeldBack(await goals.GetActiveConstructionGoalsAsync(cancellationToken));
    }
}
