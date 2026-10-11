namespace Curator.Core.Content;

/// <summary>game/content/balance.json: every tunable number (BUILD_BRIEF §6).</summary>
public sealed record Balance
{
    public required ManaBalance Mana { get; init; }

    public required MoneyBalance Money { get; init; }

    public required PatienceBalance Patience { get; init; }

    public required TrustBalance Trust { get; init; }

    public required ReputationBalance Reputation { get; init; }

    public required AlternativesBalance Alternatives { get; init; }

    public required LoansBalance Loans { get; init; }

    public required OutcomesBalance Outcomes { get; init; }

    public required WeekBalance Week { get; init; }

    public required bool Placeholder { get; init; }
}
