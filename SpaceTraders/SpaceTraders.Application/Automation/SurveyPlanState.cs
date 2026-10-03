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

    /// <summary>
    /// The targets, best first: those that need a survey (the contract's ore, then the sellable ores), then
    /// those with their stock of usable surveys (D27).
    /// </summary>
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

    /// <summary>How many usable surveys of the asteroid hold the ore.</summary>
    public int UsableSurveys { get; init; }

    /// <summary>Whether it has fewer than the stock, so a surveyor would take it (D27).</summary>
    public bool NeedsSurvey { get; init; }

    /// <summary>The surveyors working on it.</summary>
    public IReadOnlyList<string> SurveyorShipSymbols { get; init; } = [];

    /// <summary>
    /// The surveyors that can reach it in CRUISE, so the plan could give it to them: work waiting for those only (D13,
    /// <c>ShipLeftIdle</c>; B55). A target a designated surveyor's tank doesn't reach may still be one a miner can work.
    /// </summary>
    public IReadOnlyList<string> CandidateShipSymbols { get; init; } = [];
}
