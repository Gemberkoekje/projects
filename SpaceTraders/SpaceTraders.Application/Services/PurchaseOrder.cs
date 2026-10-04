using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Services;

/// <summary>
/// The order ships are bought in (PLAN.md slice 6.10b, D43). Every plan that buys ships says on each pass what it would
/// buy (<see cref="PurchaseNeed"/>), and buys it only when nothing comes before it:
/// <list type="number">
///   <item>the contract's drone (D23, D40);</item>
///   <item>a designated surveyor for each system with miners (D47);</item>
///   <item>a drone for each SCARCE or LIMITED mineral, until there is one per mineral and area (D48, D53);</item>
///   <item>one more surveyor for each area with mining drones that has none (D55);</item>
///   <item>the cargo ships of <c>Trade.ShipPurchases</c> (D21), saved up for: while one is still to buy, no probe and no
///   other drone is bought;</item>
///   <item>the jump gate's next load of materials (slice 6.6, D64), while the gate needs materials and a ship has the
///   construction role: no ship, but materials that are spent for good, so it keeps the credit reserve as a ship does, and
///   the probes and further ships wait until the gate is done;</item>
///   <item>a probe for every market (D29);</item>
///   <item>then drones by the miners' rule (D28, D32) and cargo ships of the list's last type, in turn: a drone, a cargo
///   ship, and so on, the kind not bought last. A turn passes when the other kind has nothing to buy.</item>
/// </list>
/// A need counts while its plan is on, and only while it can be met (its plan's cap not reached, a known shipyard selling
/// the ship): a need that never can be would stop everything after it. Until each plan that is on, and could need
/// something earlier, has said what it needs within <see cref="PurchaseNeeds.Lifetime"/>, nothing after it is bought: after
/// a start, and after a pause in which no plan ran (a 502 pauses them for 3 minutes), the plans say again before anything
/// is bought. The order says who may buy; the credit reserve stays the purchase's own check (<see cref="IShipPurchaseService"/>).
/// </summary>
public interface IPurchaseOrder
{
    /// <summary>Records what the plan would buy this pass, and says whether it may buy it now.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="need">What it would buy; <see cref="PurchaseNeed.None"/> for nothing.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>True when nothing comes before it in the order; false for <see cref="PurchaseNeed.None"/>.</returns>
    Task<bool> ReportAsync(AutomationPlan plan, PurchaseNeed need, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class PurchaseOrder(
    PurchaseNeeds needs,
    ISettingsRepository settings,
    ILedgerRepository ledger,
    ILogger<PurchaseOrder> logger) : IPurchaseOrder
{
    /// <summary>The plans that buy ships, each with the earliest place in the order its need can take.</summary>
    internal static readonly IReadOnlyDictionary<AutomationPlan, PurchaseTier> BuyingPlans = new Dictionary<AutomationPlan, PurchaseTier>
    {
        [AutomationPlan.Contract] = PurchaseTier.Contract,
        [AutomationPlan.Survey] = PurchaseTier.Surveyor,
        [AutomationPlan.Mining] = PurchaseTier.Coverage,
        [AutomationPlan.Siphon] = PurchaseTier.Coverage,
        [AutomationPlan.Trading] = PurchaseTier.CargoShips,
        [AutomationPlan.Construction] = PurchaseTier.Construction,
        [AutomationPlan.ProbeDeployment] = PurchaseTier.Probes,
    };

    /// <summary>The drones the plans buy, which take turns with the cargo ships.</summary>
    private static readonly IReadOnlySet<ShipType> DroneTypes = new HashSet<ShipType> { ShipType.ShipMiningDrone, ShipType.ShipSiphonDrone };

    /// <summary>
    /// The cargo ships that take turns with the drones, besides the list's own types: the game's freighters, so the ones
    /// bought before the list was changed still count.
    /// </summary>
    private static readonly IReadOnlySet<ShipType> CargoShipTypes = new HashSet<ShipType> { ShipType.ShipLightShuttle, ShipType.ShipLightHauler, ShipType.ShipHeavyFreighter };

    /// <inheritdoc />
    public Task<bool> ReportAsync(AutomationPlan plan, PurchaseNeed need, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(need);
        return DecideAsync(plan, need, cancellationToken);
    }

    /// <summary>
    /// What comes before a plan's need in the order (D43): each other plan that is on whose need comes earlier, or, between
    /// drones and cargo ships, is of the kind whose turn it is; and each other plan that is on, could need something
    /// earlier, and hasn't said what it needs within <see cref="PurchaseNeeds.Lifetime"/>.
    /// </summary>
    /// <param name="plan">The plan that would buy.</param>
    /// <param name="need">What it would buy.</param>
    /// <param name="reported">What each plan said it needs, as it last said it, since the start.</param>
    /// <param name="plansOn">The plans that buy ships and are on.</param>
    /// <param name="turn">Whose turn it is between drones and cargo ships (<see cref="Turn"/>).</param>
    /// <param name="now">The time to judge by.</param>
    /// <returns>What comes first, in words, for the log; empty when the plan may buy.</returns>
    internal static IReadOnlyList<string> Ahead(
        AutomationPlan plan,
        PurchaseNeed need,
        IReadOnlyDictionary<AutomationPlan, ReportedNeed> reported,
        IReadOnlySet<AutomationPlan> plansOn,
        PurchaseKind turn,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(reported);
        ArgumentNullException.ThrowIfNull(plansOn);

        var kind = KindOf(plan);
        var ahead = new List<string>();
        foreach (var other in plansOn.Where(other => other != plan).Order())
        {
            if (!reported.TryGetValue(other, out var report) || now - report.At > PurchaseNeeds.Lifetime)
            {
                // Not heard from since the start, or since a pause: what it needs could come first.
                if (BuyingPlans.TryGetValue(other, out var earliest) && earliest < need.Tier)
                {
                    ahead.Add($"the {other} plan, not heard from lately");
                }

                continue;
            }

            var open = report.Need;
            if (open.Tier == PurchaseTier.None)
            {
                continue;
            }

            var otherKind = KindOf(other);
            if (open.Tier < need.Tier
                || (open.Tier == PurchaseTier.Alternating
                    && need.Tier == PurchaseTier.Alternating
                    && otherKind != kind
                    && otherKind == turn))
            {
                ahead.Add($"the {other} plan's {open.ShipType} ({open.Tier})");
            }
        }

        return ahead;
    }

    /// <summary>
    /// Whose turn it is between drones and cargo ships (D43): the kind not bought last, so after the list's last cargo ship a
    /// drone comes first, then a cargo ship, and so on; the drones' when neither was ever bought. Any drone counts, the
    /// contract's and a scarce mineral's too; probes and surveyors don't take turns. A turn that passed because one kind
    /// had nothing to buy isn't made up later, so a kind never gets a run of turns, and an edited list or a lost ledger row
    /// can't keep one kind waiting.
    /// </summary>
    /// <param name="purchases">The ships bought, the oldest first.</param>
    /// <param name="list">The types in <c>Trade.ShipPurchases</c>, in order: cargo ships, besides the game's freighters.</param>
    /// <returns>The kind whose turn it is.</returns>
    internal static PurchaseKind Turn(IReadOnlyList<PurchaseRecord> purchases, IReadOnlyList<ShipType> list)
    {
        ArgumentNullException.ThrowIfNull(purchases);
        ArgumentNullException.ThrowIfNull(list);

        var cargoTypes = CargoShipTypes.Concat(list.Where(type => type != ShipType.None)).ToHashSet();
        for (var index = purchases.Count - 1; index >= 0; index--)
        {
            if (DroneTypes.Contains(purchases[index].Type))
            {
                return PurchaseKind.CargoShip;
            }

            if (cargoTypes.Contains(purchases[index].Type))
            {
                return PurchaseKind.Drone;
            }
        }

        return PurchaseKind.Drone;
    }

    /// <summary>The kind of ship a plan buys when drones and cargo ships take turns.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns><see cref="PurchaseKind.Drone"/> for the mining and siphon plans, <see cref="PurchaseKind.CargoShip"/> for the trading plan.</returns>
    internal static PurchaseKind KindOf(AutomationPlan plan) => plan switch
    {
        AutomationPlan.Mining or AutomationPlan.Siphon => PurchaseKind.Drone,
        AutomationPlan.Trading => PurchaseKind.CargoShip,
        _ => PurchaseKind.None,
    };

    private async Task<bool> DecideAsync(AutomationPlan plan, PurchaseNeed need, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        needs.Report(plan, need, now);
        if (need.Tier == PurchaseTier.None)
        {
            return false;
        }

        var plansOn = new HashSet<AutomationPlan> { plan };
        foreach (var buyer in BuyingPlans.Keys.Where(buyer => buyer != plan))
        {
            if (await settings.IsPlanEnabledAsync(buyer, cancellationToken))
            {
                plansOn.Add(buyer);
            }
        }

        var turn = need.Tier == PurchaseTier.Alternating ? await TurnAsync(cancellationToken) : PurchaseKind.None;
        var ahead = Ahead(plan, need, needs.Reported(), plansOn, turn, now);
        if (ahead.Count == 0)
        {
            return true;
        }

        logger.LogDebug(
            "Purchase order: the {Plan} plan's {ShipType} ({Tier}) waits for {Ahead} (D43).",
            plan,
            need.ShipType,
            need.Tier,
            string.Join("; ", ahead));
        return false;
    }

    /// <summary>Whose turn it is between drones and cargo ships, from the purchases (the ledger's, once) and the list.</summary>
    private async Task<PurchaseKind> TurnAsync(CancellationToken cancellationToken)
    {
        if (!needs.Seeded)
        {
            var rows = await ledger.GetRangeAsync(category: LedgerCategory.ShipPurchase, limit: 1_000, cancellationToken: cancellationToken);
            needs.Seed(rows.Select(row => new PurchaseRecord(
                row.ShipSymbol,
                Enum.TryParse<ShipType>(row.GoodSymbol, ignoreCase: true, out var type) ? type : ShipType.None,
                row.OccurredAt)));
        }

        List<ShipType> list =
        [
            .. (await settings.GetAsync<string>(TradingAutomationService.ShipPurchasesSetting, cancellationToken) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ShipPurchaseService.ToShipType),
        ];
        return Turn(needs.Purchases(), list);
    }
}

/// <summary>
/// What each plan that buys ships would buy, as it last said, and the ships bought (PLAN.md slice 6.10b, D43): what
/// <see cref="PurchaseOrder"/> decides by. In memory, a singleton: a restart forgets both, so the plans say again on their
/// next pass, and the purchases come back from the ledger, which keeps them longer than a reset lasts.
/// </summary>
/// <remarks>Thread-safe: a purchase through the control endpoint can come at any time.</remarks>
public sealed class PurchaseNeeds
{
    /// <summary>
    /// How long what a plan said counts. A plan that is on and hasn't said what it needs for longer holds back what could come
    /// after it, as at a start, until it says again: after a pause every plan says again before anything is bought, and a
    /// plan that fails before it says holds the purchases after it. A plan that is switched off holds back nothing.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly Lock _gate = new();
    private readonly Dictionary<AutomationPlan, ReportedNeed> _needs = [];
    private readonly Dictionary<string, PurchaseRecord> _purchases = new(StringComparer.OrdinalIgnoreCase);
    private bool _seeded;

    /// <summary>Whether the purchases were read from the ledger since the start.</summary>
    public bool Seeded
    {
        get
        {
            lock (_gate)
            {
                return _seeded;
            }
        }
    }

    /// <summary>Records what a plan would buy now.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="need">What it would buy; <see cref="PurchaseNeed.None"/> for nothing.</param>
    /// <param name="at">When it said it.</param>
    public void Report(AutomationPlan plan, PurchaseNeed need, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(need);
        lock (_gate)
        {
            _needs[plan] = new ReportedNeed(need, at);
        }
    }

    /// <summary>What each plan said it needs, as it last said it, since the start.</summary>
    /// <returns>The needs by plan, open or not.</returns>
    public IReadOnlyDictionary<AutomationPlan, ReportedNeed> Reported()
    {
        lock (_gate)
        {
            return new Dictionary<AutomationPlan, ReportedNeed>(_needs);
        }
    }

    /// <summary>The needs that count at <paramref name="now"/>, first in the order first, for the metrics.</summary>
    /// <param name="now">The time to judge by.</param>
    /// <returns>Each plan's open need.</returns>
    public IReadOnlyList<(AutomationPlan Plan, PurchaseNeed Need)> Open(DateTimeOffset now)
    {
        lock (_gate)
        {
            return [.. _needs
                .Where(entry => entry.Value.Need.Tier != PurchaseTier.None && now - entry.Value.At <= Lifetime)
                .OrderBy(entry => entry.Value.Need.Tier)
                .ThenBy(entry => entry.Key)
                .Select(entry => (entry.Key, entry.Value.Need))];
        }
    }

    /// <summary>Records a ship bought in this process, which the ledger may not hold yet.</summary>
    /// <param name="shipSymbol">The new ship.</param>
    /// <param name="type">Its type.</param>
    /// <param name="at">When it was bought.</param>
    public void Bought(string shipSymbol, ShipType type, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipSymbol);
        lock (_gate)
        {
            _purchases[shipSymbol] = new PurchaseRecord(shipSymbol, type, at);
        }
    }

    /// <summary>Adds the purchases the ledger holds; one already recorded in this process stays as it is.</summary>
    /// <param name="purchases">The ledger's ship purchases.</param>
    public void Seed(IEnumerable<PurchaseRecord> purchases)
    {
        ArgumentNullException.ThrowIfNull(purchases);
        lock (_gate)
        {
            foreach (var purchase in purchases.Where(purchase => !string.IsNullOrWhiteSpace(purchase.ShipSymbol)))
            {
                _purchases.TryAdd(purchase.ShipSymbol, purchase);
            }

            _seeded = true;
        }
    }

    /// <summary>The ships bought, the oldest first.</summary>
    /// <returns>The purchases.</returns>
    public IReadOnlyList<PurchaseRecord> Purchases()
    {
        lock (_gate)
        {
            return [.. _purchases.Values.OrderBy(purchase => purchase.At).ThenBy(purchase => purchase.ShipSymbol, StringComparer.Ordinal)];
        }
    }
}

/// <summary>Where a purchase stands in the order ships are bought in (D43): a lower tier comes first.</summary>
public enum PurchaseTier
{
    /// <summary>Nothing to buy.</summary>
    None = 0,

    /// <summary>The contract's drone, while no miner is free for the contract (D23, D40).</summary>
    Contract = 1,

    /// <summary>A designated surveyor for a system with miners (D47).</summary>
    Surveyor = 2,

    /// <summary>A drone for a SCARCE or LIMITED mineral, until there is one drone per such mineral and area (D48, D53).</summary>
    Coverage = 3,

    /// <summary>One more surveyor, until each area with mining drones has one (D55).</summary>
    SurveyorPerArea = 4,

    /// <summary>The next cargo ship of <c>Trade.ShipPurchases</c> (D21), saved up for.</summary>
    CargoShips = 5,

    /// <summary>
    /// The jump gate's next load of materials (slice 6.6, D64): not a ship, but spent for good, so it keeps the credit
    /// reserve as one does. While the gate needs materials, everything after it waits.
    /// </summary>
    Construction = 6,

    /// <summary>A probe, until every market has one (D29).</summary>
    Probes = 7,

    /// <summary>A drone by the miners' rule (D28, D32), or one more cargo ship of the list's last type, in turn.</summary>
    Alternating = 8,
}

/// <summary>The kinds of ship that take turns once everything before them is bought (D43).</summary>
public enum PurchaseKind
{
    /// <summary>Neither: the ship doesn't take turns.</summary>
    None = 0,

    /// <summary>A mining or siphon drone.</summary>
    Drone = 1,

    /// <summary>A cargo ship.</summary>
    CargoShip = 2,
}

/// <summary>What a plan would buy now, if the order and the credits let it (D43).</summary>
public sealed record PurchaseNeed
{
    /// <summary>Nothing to buy.</summary>
    public static readonly PurchaseNeed None = new(PurchaseTier.None, string.Empty, string.Empty, 0);

    /// <summary>Creates a need.</summary>
    /// <param name="Tier">Where it stands in the order.</param>
    /// <param name="ShipType">The ship, such as <c>SHIP_SURVEYOR</c>.</param>
    /// <param name="ShipyardWaypointSymbol">Where it would be bought.</param>
    /// <param name="Price">What the shipyard asks for it, as cached.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PurchaseNeed(PurchaseTier Tier, string ShipType, string ShipyardWaypointSymbol, long Price)
    {
        this.Tier = Tier;
        this.ShipType = ShipType;
        this.ShipyardWaypointSymbol = ShipyardWaypointSymbol;
        this.Price = Price;
    }

    /// <summary>Where it stands in the order.</summary>
    public required PurchaseTier Tier { get; init; }

    /// <summary>The ship, such as <c>SHIP_SURVEYOR</c>.</summary>
    public required string ShipType { get; init; }

    /// <summary>Where it would be bought.</summary>
    public required string ShipyardWaypointSymbol { get; init; }

    /// <summary>What the shipyard asks for it, as cached.</summary>
    public required long Price { get; init; }
}

/// <summary>A need as a plan last said it.</summary>
public sealed record ReportedNeed
{
    /// <summary>Creates a reported need.</summary>
    /// <param name="Need">What the plan would buy.</param>
    /// <param name="At">When it said it.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ReportedNeed(PurchaseNeed Need, DateTimeOffset At)
    {
        this.Need = Need;
        this.At = At;
    }

    /// <summary>What the plan would buy.</summary>
    public required PurchaseNeed Need { get; init; }

    /// <summary>When it said it.</summary>
    public required DateTimeOffset At { get; init; }
}

/// <summary>A ship bought.</summary>
public sealed record PurchaseRecord
{
    /// <summary>Creates a purchase.</summary>
    /// <param name="ShipSymbol">The ship.</param>
    /// <param name="Type">Its type; <see cref="ShipType.None"/> when the ledger names one this version doesn't know.</param>
    /// <param name="At">When it was bought.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PurchaseRecord(string ShipSymbol, ShipType Type, DateTimeOffset At)
    {
        this.ShipSymbol = ShipSymbol;
        this.Type = Type;
        this.At = At;
    }

    /// <summary>The ship.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>Its type.</summary>
    public required ShipType Type { get; init; }

    /// <summary>When it was bought.</summary>
    public required DateTimeOffset At { get; init; }
}
