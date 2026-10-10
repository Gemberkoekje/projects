using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
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
///   the probes and further ships wait until the gate is done. In the same place, a mining drone for the gate's smelters
///   (slice 6.25, D92), which waits while a load can be bought: "If the gate can be built, it should be built, otherwise
///   extra miners can be built.";</item>
///   <item>every explorer the explore plan wants (slice 6.30, D98, D102: one for every 10 systems left to explore), the first
///   and every further one before the probes (slice 6.33, D113); the command ship fetches one no probe of ours can (D108);</item>
///   <item>one more cargo ship with the largest hold, once <c>Trade.ShipPurchaseIntervalMinutes</c> have passed since the trading
///   plan last bought one (slice 6.34, D116): "once every half hour, money permitting, a trade ship is bought, independent on
///   whether probes still need to be bought";</item>
///   <item>a probe for every market (D29): home's, then those of the explored systems within the trade reach (slice 6.28,
///   D97);</item>
///   <item>then drones by the miners' rule (D28, D32) and cargo ships with the largest hold (D112), in turn: a drone, a cargo
///   ship, and so on, the kind not bought last. A turn passes when the other kind has nothing to buy;</item>
///   <item>a probe for every market of the other explored systems (slice 6.28, D97);</item>
///   <item>last, a miner for a system abroad, one per ore its markets are short of (slice 6.40, D122): "outside of the home area,
///   mining is low priority, and should never be in the way of trading".</item>
/// </list>
/// A need counts while its plan is on, and only while it can be met (its plan's cap not reached, a known shipyard selling
/// the ship): a need that never can be would stop everything after it. Until each plan that is on, and could need
/// something earlier, has said what it needs in this tick or the one before, or within <see cref="PurchaseNeeds.Lifetime"/>
/// (<see cref="PurchaseNeeds.Counts"/>, B79), nothing after it is bought: after a start, and after a pause in which no plan
/// ran (a 502 pauses them for 3 minutes, while the ticks go on), the plans say again before anything is bought. The order says who may buy; the credit reserve stays the purchase's own check (<see cref="IShipPurchaseService"/>).
/// Since slice 6.36 (D118) a need whose purchase waits only for one of our ships to reach its shipyard (D30) lets the needs
/// after it go first, each as long as the credits after it leave the waiting ones' prices and the credit reserve, which the
/// purchase keeps too (<see cref="PurchaseNeeds.HeldFor"/>); one that waits for credits still holds them back.
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
    ShipyardCalls calls,
    IBudgetPolicy budget,
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
        [AutomationPlan.Explore] = PurchaseTier.Explorer,
        [AutomationPlan.ProbeDeployment] = PurchaseTier.Probes,
    };

    /// <summary>
    /// The drones the plans buy, which take turns with the cargo ships: the ore hounds bought in a mining drone's place too (slice
    /// 6.39, D120).
    /// </summary>
    private static readonly IReadOnlySet<ShipType> DroneTypes = new HashSet<ShipType> { ShipType.ShipMiningDrone, ShipType.ShipOreHound, ShipType.ShipSiphonDrone };

    /// <summary>
    /// The ships bought that aren't cargo ships taking turns with the drones: the drones themselves, the probes and the
    /// interceptors bought in their place (slice 6.38, D119), the surveyors and the explorers, and a type this version doesn't
    /// know. Every other type is one: the list's, and since slice 6.33 (D112) whichever has the largest hold beyond it, such as
    /// a heavy or bulk freighter, so the drones' turn comes after each.
    /// </summary>
    private static readonly IReadOnlySet<ShipType> NotCargoShipTypes = new HashSet<ShipType>
    {
        ShipType.None,
        ShipType.ShipProbe,
        ShipType.ShipInterceptor,
        ShipType.ShipMiningDrone,
        ShipType.ShipOreHound,
        ShipType.ShipSiphonDrone,
        ShipType.ShipSurveyor,
        ShipType.ShipExplorer,
    };

    /// <inheritdoc />
    public Task<bool> ReportAsync(AutomationPlan plan, PurchaseNeed need, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(need);
        return DecideAsync(plan, need, cancellationToken);
    }

    /// <summary>
    /// What comes before a plan's need in the order (D43): each other plan that is on whose need comes earlier, or, between
    /// drones and cargo ships, is of the kind whose turn it is; and each other plan that is on, could need something
    /// earlier, and hasn't said what it needs lately (<see cref="PurchaseNeeds.Counts"/>). At the jump gate's place, the gate's
    /// load comes before the gate's miners unless it waits for its markets (D92), and so does the construction plan while
    /// it hasn't said what it needs.
    /// </summary>
    /// <param name="plan">The plan that would buy.</param>
    /// <param name="need">What it would buy.</param>
    /// <param name="reported">What each plan said it needs, as it last said it, since the start.</param>
    /// <param name="plansOn">The plans that buy ships and are on.</param>
    /// <param name="turn">Whose turn it is between drones and cargo ships (<see cref="Turn"/>).</param>
    /// <param name="counts">Whether what a plan said at a time still counts (<see cref="PurchaseNeeds.Counts"/>).</param>
    /// <returns>What comes first; empty when the plan may buy.</returns>
    internal static IReadOnlyList<Before> Ahead(
        AutomationPlan plan,
        PurchaseNeed need,
        IReadOnlyDictionary<AutomationPlan, ReportedNeed> reported,
        IReadOnlySet<AutomationPlan> plansOn,
        PurchaseKind turn,
        Func<DateTimeOffset, bool> counts)
    {
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(reported);
        ArgumentNullException.ThrowIfNull(plansOn);
        ArgumentNullException.ThrowIfNull(counts);

        var kind = KindOf(plan);
        var ahead = new List<Before>();
        foreach (var other in plansOn.Where(other => other != plan).Order())
        {
            if (!reported.TryGetValue(other, out var report) || !counts(report.At))
            {
                // Not heard from since the start, or since a pause: what it needs could come first. The gate's load could be
                // one it may buy now (D92).
                if (BuyingPlans.TryGetValue(other, out var earliest)
                    && (earliest < need.Tier || (earliest == need.Tier && IsGateLoadBeforeMiners(other, plan, need))))
                {
                    ahead.Add(new Before(other, PurchaseNeed.None));
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
                    && otherKind == turn)
                || (open.Tier == need.Tier && !open.WaitsForMarkets && IsGateLoadBeforeMiners(other, plan, need)))
            {
                ahead.Add(new Before(other, open));
            }
        }

        return ahead;
    }

    /// <summary>
    /// The plans whose purchase waits for one of our ships at its shipyard (D30): what each said it needs, lately
    /// (<see cref="PurchaseNeeds.Counts"/>), is a ship at a shipyard with an open call for that ship (<see cref="ShipyardCalls"/>),
    /// which the purchase makes when no ship of ours is there. Slice 6.36 (D118): such a purchase lets the ones after it go first.
    /// </summary>
    /// <param name="reported">What each plan said it needs, as it last said it.</param>
    /// <param name="calls">The shipyards where a purchase waits for one of our ships.</param>
    /// <param name="counts">Whether what a plan said at a time still counts (<see cref="PurchaseNeeds.Counts"/>).</param>
    /// <returns>The plans.</returns>
    internal static IReadOnlySet<AutomationPlan> WaitingForAShip(
        IReadOnlyDictionary<AutomationPlan, ReportedNeed> reported,
        IReadOnlyList<ShipyardCall> calls,
        Func<DateTimeOffset, bool> counts)
    {
        ArgumentNullException.ThrowIfNull(reported);
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(counts);

        return reported
            .Where(entry => entry.Value.Need.Tier != PurchaseTier.None
                && counts(entry.Value.At)
                && calls.Any(call => call.WaypointSymbol.Equals(entry.Value.Need.ShipyardWaypointSymbol, StringComparison.OrdinalIgnoreCase)
                    && call.ShipType.Equals(entry.Value.Need.ShipType, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.Key)
            .ToHashSet();
    }

    /// <summary>
    /// Whose turn it is between drones and cargo ships (D43): the kind not bought last, so after the list's last cargo ship a
    /// drone comes first, then a cargo ship, and so on; the drones' when neither was ever bought. Any drone counts, the
    /// contract's and a scarce mineral's too; probes, surveyors and explorers don't take turns, and every other ship is a cargo
    /// ship, whichever type the trading plan bought for its hold (D112). A turn that passed because one kind had nothing to buy
    /// isn't made up later, so a kind never gets a run of turns, and an edited list or a lost ledger row can't keep one kind
    /// waiting.
    /// </summary>
    /// <param name="purchases">The ships bought, the oldest first.</param>
    /// <param name="list">The types in <c>Trade.ShipPurchases</c>, in order: cargo ships, whatever their type.</param>
    /// <returns>The kind whose turn it is.</returns>
    internal static PurchaseKind Turn(IReadOnlyList<PurchaseRecord> purchases, IReadOnlyList<ShipType> list)
    {
        ArgumentNullException.ThrowIfNull(purchases);
        ArgumentNullException.ThrowIfNull(list);

        var listed = list.Where(type => type != ShipType.None).ToHashSet();
        for (var index = purchases.Count - 1; index >= 0; index--)
        {
            if (DroneTypes.Contains(purchases[index].Type))
            {
                return PurchaseKind.CargoShip;
            }

            if (listed.Contains(purchases[index].Type) || !NotCargoShipTypes.Contains(purchases[index].Type))
            {
                return PurchaseKind.Drone;
            }
        }

        return PurchaseKind.Drone;
    }

    /// <summary>
    /// Whether another plan's need at the jump gate's place comes before this one (PLAN.md slice 6.25, D92): the gate's load
    /// before the gate's miners, which the mining plan buys there. Asked on 2026-10-05: "If the gate can be built, it should be
    /// built, otherwise extra miners can be built." The miners never hold back the load.
    /// </summary>
    /// <param name="other">The plan whose need could come first.</param>
    /// <param name="plan">The plan that would buy.</param>
    /// <param name="need">What it would buy.</param>
    /// <returns>True when <paramref name="other"/> is the construction plan, and <paramref name="need"/> another plan's at its place.</returns>
    private static bool IsGateLoadBeforeMiners(AutomationPlan other, AutomationPlan plan, PurchaseNeed need)
        => other == AutomationPlan.Construction && plan != AutomationPlan.Construction && need.Tier == PurchaseTier.Construction;

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
        var reported = needs.Reported();
        bool Counts(DateTimeOffset saidAt) => needs.Counts(saidAt, now);
        var ahead = Ahead(plan, need, reported, plansOn, turn, Counts);
        if (ahead.Count == 0)
        {
            return true;
        }

        // Slice 6.36 (D118), asked on 2026-10-07: "Yes please, as long as the total doesn't dip below the total needed for the
        // freighter." A purchase that waits only for one of our ships to reach its shipyard lets this one go first, as long as
        // the credits after it leave the waiting ones' prices and the credit reserve; its purchase keeps them too.
        var waiting = WaitingForAShip(reported, calls.Open(now), Counts);
        if (ahead.All(before => before.Need.Tier != PurchaseTier.None && waiting.Contains(before.Plan)))
        {
            var held = ahead.Sum(before => before.Need.Price);
            if ((await budget.EvaluateAsync(need.Price + held, cancellationToken)).CanAfford)
            {
                needs.Hold(plan, held);
                logger.LogDebug(
                    "Purchase order: the {Plan} plan's {ShipType} ({Tier}) goes before {Ahead}, which wait for one of our ships at their shipyards, and leaves {Held} for them (D118).",
                    plan,
                    need.ShipType,
                    need.Tier,
                    string.Join("; ", ahead),
                    held);
                return true;
            }
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
    /// How long what a plan said counts at least; it counts longer while its next pass hasn't come, in this tick or the one
    /// before (<see cref="GameTicks.Counts"/>, B79). A plan that is on and hasn't said what it needs since holds back what could
    /// come after it, as at a start, until it says again: after a pause every plan says again before anything is bought, and a
    /// plan that fails before it says holds the purchases after it. A plan that is switched off holds back nothing.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly GameTicks _ticks;
    private readonly Lock _gate = new();
    private readonly Dictionary<AutomationPlan, ReportedNeed> _needs = [];
    private readonly Dictionary<string, PurchaseRecord> _purchases = new(StringComparer.OrdinalIgnoreCase);
    private bool _seeded;

    /// <summary>Creates the needs, judged by the game loop's ticks.</summary>
    /// <param name="ticks">When the ticks began.</param>
    public PurchaseNeeds(GameTicks ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        _ticks = ticks;
    }

    /// <summary>Creates the needs with no ticks known: what a plan said counts for <see cref="Lifetime"/>.</summary>
    public PurchaseNeeds()
        : this(new GameTicks())
    {
    }

    /// <summary>Whether what a plan said at <paramref name="saidAt"/> still counts at <paramref name="now"/> (B79).</summary>
    /// <param name="saidAt">When the plan said it.</param>
    /// <param name="now">The time to judge by.</param>
    /// <returns>True while it counts.</returns>
    public bool Counts(DateTimeOffset saidAt, DateTimeOffset now) => _ticks.Counts(saidAt, now, Lifetime);

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

    /// <summary>
    /// Records that the order let a plan's need go before needs whose purchase waits for one of our ships (slice 6.36, D118),
    /// and what its purchase keeps for them: their prices.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="held">The credits its purchase keeps, beyond the credit reserve.</param>
    public void Hold(AutomationPlan plan, long held)
    {
        lock (_gate)
        {
            if (_needs.TryGetValue(plan, out var reported))
            {
                _needs[plan] = reported with { Held = held };
            }
        }
    }

    /// <summary>
    /// What a purchase keeps, beyond the credit reserve, for the needs before it in the order that wait for one of our ships
    /// (slice 6.36, D118): what the order let the need for that ship at that shipyard through with, when what it said still
    /// counts (<see cref="Counts"/>); 0 when nothing waited before it.
    /// </summary>
    /// <param name="shipType">The ship, such as <c>SHIP_PROBE</c>.</param>
    /// <param name="shipyardWaypointSymbol">Where it is bought.</param>
    /// <param name="now">The time to judge by.</param>
    /// <returns>The credits to keep.</returns>
    public long HeldFor(string shipType, string shipyardWaypointSymbol, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(shipType);
        ArgumentNullException.ThrowIfNull(shipyardWaypointSymbol);
        lock (_gate)
        {
            return _needs.Values
                .Where(reported => Counts(reported.At, now)
                    && reported.Need.ShipType.Equals(shipType, StringComparison.OrdinalIgnoreCase)
                    && reported.Need.ShipyardWaypointSymbol.Equals(shipyardWaypointSymbol, StringComparison.OrdinalIgnoreCase))
                .Select(reported => reported.Held)
                .DefaultIfEmpty(0)
                .Max();
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
                .Where(entry => entry.Value.Need.Tier != PurchaseTier.None && Counts(entry.Value.At, now))
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
    /// reserve as one does. While the gate needs materials, everything after it waits. A mining drone for the gate's smelters
    /// stands here too (slice 6.25, D92), after a load that can be bought now (<see cref="PurchaseNeed.WaitsForMarkets"/>).
    /// </summary>
    Construction = 6,

    /// <summary>
    /// Every explorer the explore plan wants (slice 6.30, D98: "Start with one before probes"; D102: one for every 10 systems left
    /// to explore). Asked on 2026-10-07, slice 6.33 (D113): "I'd like Explorers (order 10) to go in front of probes (order 8)",
    /// so the further ones too, which stood after the drones and cargo ships that take turns. The command ship fetches one that no
    /// probe of ours can (D108).
    /// </summary>
    Explorer = 7,

    /// <summary>
    /// One more cargo ship, the largest hold (D112), once <c>Trade.ShipPurchaseIntervalMinutes</c> have passed since the trading
    /// plan last bought one (slice 6.34, D116). Asked on 2026-10-07: "I would like to switch priorities between new trade ships
    /// and probes. So once every half hour, money permitting, a trade ship is bought, independent on whether probes still need
    /// to be bought." Only while D88's route has waited for it; till the half hour has passed, the next takes its turn with the
    /// drones (<see cref="Alternating"/>).
    /// </summary>
    TimedCargoShip = 8,

    /// <summary>
    /// A probe, until every market has one (D29): home's, then those of the explored systems within the trade reach,
    /// <c>Trade.MaxHaulDistance</c> jumps of home (slice 6.28, D96, D97). 8 until slice 6.34 (D116) put the cargo ship on the
    /// clock before it.
    /// </summary>
    Probes = 9,

    /// <summary>
    /// A drone by the miners' rule (D28, D32), or one more cargo ship, in turn: the ship with the largest hold a shipyard within
    /// the trade reach lists, not SCARCE there (slice 6.33, D112).
    /// </summary>
    Alternating = 10,

    /// <summary>
    /// A probe for a market of an explored system beyond the trade reach (slice 6.28, D97): "all explored markets when money
    /// allows", last, after the drones and cargo ships that take turns. 11 until slice 6.33 (D113) moved the further explorers,
    /// which stood at 10, to <see cref="Explorer"/>, and 11 again since slice 6.34 (D116) put the cargo ship on the clock at 8.
    /// </summary>
    FarProbes = 11,

    /// <summary>
    /// A miner for a system abroad (slice 6.40, D122), an ore hound where one is sold, else a mining drone (D120): one per ore a
    /// market there that makes something from it has SCARCE or LIMITED, last of all. Asked on 2026-10-09: "I'd like mining to be
    /// done wherever there's low ore supply, not just in the home area. However, outside of the home area, mining is low
    /// priority, and should never be in the way of trading, which is generally more lucrative."
    /// </summary>
    MiningAbroad = 12,
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

    /// <summary>
    /// Whether it can't be bought yet, whatever the credits: the jump gate's next load while every market that sells it has it
    /// SCARCE or LIMITED (D66), or another trip on its way to buy it there (D80). The gate's miners may be bought meanwhile, at
    /// the gate's place in the order (slice 6.25, D92). False unless set.
    /// </summary>
    public bool WaitsForMarkets { get; init; }
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

    /// <summary>
    /// What its purchase keeps beyond the credit reserve: the prices of the needs before it that wait for one of our ships, when
    /// the order let it go before them (slice 6.36, D118, <see cref="PurchaseNeeds.Hold"/>); 0 otherwise.
    /// </summary>
    public long Held { get; init; }
}

/// <summary>
/// A plan whose need comes before another's in the order (D43): what it said it needs, or <see cref="PurchaseNeed.None"/> when it
/// hasn't said so lately (<see cref="PurchaseNeeds.Counts"/>), and anything it could need may come first.
/// </summary>
/// <param name="Plan">The plan.</param>
/// <param name="Need">What it said it needs; <see cref="PurchaseNeed.None"/> when not heard from lately.</param>
internal sealed record Before(AutomationPlan Plan, PurchaseNeed Need)
{
    /// <summary>The plan and its need in words, for the log.</summary>
    /// <returns>Such as "the Trading plan's SHIP_HEAVY_FREIGHTER (TimedCargoShip)".</returns>
    public override string ToString()
        => Need.Tier == PurchaseTier.None ? $"the {Plan} plan, not heard from lately" : $"the {Plan} plan's {Need.ShipType} ({Need.Tier})";
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
