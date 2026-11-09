namespace SpaceTraders.Application.Automation;

public static partial class PlanTypes
{
    public const string Construction = "Construction";
}

/// <summary>
/// The construction plan's view after its last pass (PLAN.md slice 6.6): the jump gates that need materials, with what
/// each still needs and what is on its way; the ships that build them; and why no load was bought, when none was. Written
/// only when it changes. The <c>ShipLeftIdle</c> rule reads <see cref="ReadyShipSymbols"/>.
/// </summary>
public sealed record ConstructionPlanState
{
    /// <summary>The construction sites that need materials, in the systems where our ships are.</summary>
    public required IReadOnlyList<ConstructionSiteState> Sites { get; init; }

    /// <summary>The ships that build: with the role board on, those with the construction role (D65).</summary>
    public IReadOnlyList<string> BuilderShipSymbols { get; init; } = [];

    /// <summary>
    /// The builders a load waits for, on a trip or not: the order ships are bought in lets construction buy, and the credits
    /// pay for a load from where the builder is going. The plan gives a free one its load at once, so only a plan that has
    /// stopped leaves one of them idle.
    /// </summary>
    public IReadOnlyList<string> ReadyShipSymbols { get; init; } = [];

    /// <summary>
    /// Why a free builder bought no load, when one didn't: <c>purchase_order</c> (a purchase before it in the order ships are
    /// bought in, D64), <c>waiting_for_credits</c> (the load would dip into the credit reserve), <c>low_supply</c> (D66),
    /// <c>market_busy</c> (another trip is on its way to buy the material at every market that would sell a load, D80),
    /// <c>no_market</c>; empty otherwise. Meanwhile the builder trades.
    /// </summary>
    public string Waiting { get; init; } = string.Empty;

    /// <summary>
    /// What stops the home jump gate from being built, as this pass saw it (PLAN.md slice 6.51, D139): one of
    /// <see cref="SpaceTraders.Application.Construction.ConstructionBlockers"/>, the first that holds; empty in a state written before.
    /// </summary>
    public string Blocker { get; init; } = string.Empty;

    /// <summary>
    /// When the home jump gate will be finished, by the slowest of its limits (PLAN.md slice 6.55, D143,
    /// <see cref="SpaceTraders.Application.Construction.GateForecast"/>), as made at most once a minute; empty in a state
    /// written before.
    /// </summary>
    public GateForecastState Forecast { get; init; } = new();

    /// <summary>When the state was written.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One construction site as the plan saw it.</summary>
public sealed record ConstructionSiteState
{
    /// <summary>The site: the jump gate.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>Its materials.</summary>
    public required IReadOnlyList<ConstructionMaterialState> Materials { get; init; }

    /// <summary>
    /// The goods of its production chain, down to the ores, with their supply in its system (PLAN.md slice 6.51, D139,
    /// <see cref="SpaceTraders.Application.Construction.ConstructionChain"/>).
    /// </summary>
    public IReadOnlyList<ConstructionChainGoodState> Chain { get; init; } = [];
}

/// <summary>One material of a construction site.</summary>
public sealed record ConstructionMaterialState
{
    public required string TradeSymbol { get; init; }

    /// <summary>The units the site needs in all.</summary>
    public required int Required { get; init; }

    /// <summary>The units supplied so far.</summary>
    public required int Fulfilled { get; init; }

    /// <summary>The units our construction trips carry or go to buy.</summary>
    public required int OnTheWay { get; init; }
}

/// <summary>
/// One good of a jump gate's production chain (PLAN.md slice 6.51, D139): a material the gate still needs, or a good one is
/// made from, directly or further down the chains.
/// </summary>
public sealed record ConstructionChainGoodState
{
    public required string TradeSymbol { get; init; }

    /// <summary>The steps from the gate: 1 for a material, 2 for what it is made from, and so on.</summary>
    public required int Step { get; init; }

    /// <summary>
    /// The best supply among the markets of the gate's system that sell it (export or exchange it), SCARCE to ABUNDANT;
    /// empty where none does.
    /// </summary>
    public string ExportSupply { get; init; } = string.Empty;

    /// <summary>
    /// The best supply among the markets of the gate's system that make a good of the chain from it (import it and export
    /// what it goes into); empty where none does.
    /// </summary>
    public string ImportSupply { get; init; } = string.Empty;
}

/// <summary>The jump gate's forecast (PLAN.md slice 6.55, D143, <see cref="SpaceTraders.Application.Construction.GateForecast"/>).</summary>
public sealed record GateForecastState
{
    /// <summary>The hours until the gate is finished: the slowest limit.</summary>
    public double EtaHours { get; init; }

    /// <summary>The hours until the credits pay for what the gate still needs.</summary>
    public double MoneyHours { get; init; }

    /// <summary>The hours until the slowest material's sellers have given what the gate still needs of it.</summary>
    public double SupplyHours { get; init; }

    /// <summary>The hours until the builders have carried what the gate still needs.</summary>
    public double HaulHours { get; init; }

    /// <summary>The slowest limit: money, supply or haul; done when nothing is left to buy; empty before any forecast.</summary>
    public string Binding { get; init; } = string.Empty;

    /// <summary>What the units the gate still needs will cost, about.</summary>
    public long RemainingCost { get; init; }

    /// <summary>What the fleet's trades earned an hour lately.</summary>
    public double EarnedPerHour { get; init; }

    /// <summary>Each material the gate still needs, with its sellers' pace.</summary>
    public IReadOnlyList<GateForecastMaterialState> Materials { get; init; } = [];

    /// <summary>When the forecast was made.</summary>
    public DateTimeOffset ComputedAt { get; init; }
}

/// <summary>One material in the jump gate's forecast (D143).</summary>
public sealed record GateForecastMaterialState
{
    public required string TradeSymbol { get; init; }

    /// <summary>The units still needed that no trip carries or goes to buy.</summary>
    public required int Remaining { get; init; }

    /// <summary>What its sellers give an hour without falling short, about.</summary>
    public required double SupplyPerHour { get; init; }

    /// <summary>The hours until they have given what is still needed.</summary>
    public required double SupplyHours { get; init; }
}
