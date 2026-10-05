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
    /// <c>no_market</c>; empty otherwise. Meanwhile the builder trades.
    /// </summary>
    public string Waiting { get; init; } = string.Empty;

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
