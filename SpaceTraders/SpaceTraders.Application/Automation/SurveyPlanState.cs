namespace SpaceTraders.Application.Automation;

public static partial class PlanTypes
{
    public const string Survey = "Survey";
}

/// <summary>
/// The survey plan's view after its last pass (PLAN.md slice 6.4): what there is to survey, best first, and
/// which surveyor surveys what. Written only when it changes.
/// </summary>
public sealed record SurveyPlanState
{
    public required Guid PlanId { get; init; }

    /// <summary>The targets, best first: the contract's ore, then the sellable ores.</summary>
    public required IReadOnlyList<SurveyPlanTarget> Targets { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One thing to survey in the survey plan's view.</summary>
public sealed record SurveyPlanTarget
{
    /// <summary>The ore surveyed for.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>The asteroid to survey.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>The market the ore goes to: the contract's destination, or the market that pays most.</summary>
    public required string BuyerWaypointSymbol { get; init; }

    /// <summary>Whether the contract wants the ore.</summary>
    public bool ForContract { get; init; }

    /// <summary>Whether a usable survey of the asteroid holds the ore already.</summary>
    public bool HasUsableSurvey { get; init; }

    /// <summary>The surveyors working on it.</summary>
    public IReadOnlyList<string> SurveyorShipSymbols { get; init; } = [];
}
