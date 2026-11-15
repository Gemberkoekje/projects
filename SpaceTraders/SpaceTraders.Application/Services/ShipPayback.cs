using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Whether a ship bought now earns its price back before the server resets (PLAN.md slice 6.70, D150), asked on 2026-11-15:
/// "Does a ship earn it's own price back before the end of the week?" A reset ends the run with the agent, its ships and its
/// credits, so a ship that hasn't earned its price by then has cost the run that price. In the 14 hours before the reset of
/// 2026-11-15 13:00Z the bot bought 26 bulk freighters for 105M; one earned about 200,000 an hour, 20 hours for its 3.9M.
/// <list type="bullet">
///   <item>A ship pays back when what it earns an hour, from <c>Purchase.PaybackLeadHours</c> (1) after the purchase, which it
///   spends reaching its first work, until the reset, covers its price.</item>
///   <item>What it earns an hour is its type's record (<see cref="ShipNames.TypeOf"/>): what the ships of that type earned in
///   the last <see cref="Window"/>, net of cargo, fuel, antimatter and repairs, over the hours they were owned in it, once those
///   come to <see cref="MinShipHours"/>. For a drone, whose ore the shuttle sells, and for a type with a shorter record, it is
///   the role board's estimate for the role the ship is bought for (<see cref="RoleOf"/>): the average of the board's
///   estimates of the ships of its type, else of every ship with one.</item>
///   <item>Probes, interceptors and surveyors earn nothing themselves: their plans judge them by what they serve
///   (<see cref="JudgeWorthAsync"/>, slice 6.71, D151), a probe by the promise of the market it would watch
///   (<see cref="Probes.ProbeWorth"/>), a surveyor as one drone of its system (<see cref="DroneWorthAsync"/>).</item>
///   <item>With no reset known (<see cref="ServerReset"/>), or nothing known of what the type earns, a purchase isn't judged.</item>
/// </list>
/// </summary>
/// <remarks>
/// Scoped. The records are worked out at most every <see cref="RecordsLifetime"/> and kept in the memory cache, as the purchase
/// order asks for each plan's need at every pass.
/// </remarks>
public sealed class ShipPayback(
    ServerReset serverReset,
    ILedgerRepository ledger,
    IShipRepository ships,
    IPlanRepository plans,
    ISettingsRepository settings,
    IMemoryCache cache,
    ILogger<ShipPayback> logger)
{
    /// <summary>The setting that holds the hours a ship bought earns nothing, reaching its first work.</summary>
    public const string LeadSetting = "Purchase.PaybackLeadHours";

    /// <summary>The lead when the setting gives none.</summary>
    public const int DefaultLeadHours = 1;

    /// <summary>The ship-hours a type's record needs before it counts: one ship's day, or a day's worth across its ships.</summary>
    public const double MinShipHours = 24;

    /// <summary>A type's record is judged from its type's ships.</summary>
    public const string FromRecord = "record";

    /// <summary>A type's earnings are the role board's estimate.</summary>
    public const string FromBoard = "role board";

    /// <summary>A surveyor's worth: what one drone of its system mines (D151).</summary>
    public const string FromDrone = "drone's mining";

    /// <summary>A ship with no worth known, which can't reach its work before the reset (D151).</summary>
    public const string FromArrival = "arrival";

    /// <summary>How far back a type's record reaches.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>How long a working-out of the records is kept.</summary>
    public static readonly TimeSpan RecordsLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How often a plan's need that won't pay back is told at Information.</summary>
    public static readonly TimeSpan TellEvery = TimeSpan.FromHours(1);

    /// <summary>What a ship earns, and what it spends to earn it; not what is bought for the fleet or the jump gate.</summary>
    internal static readonly IReadOnlyList<LedgerCategory> EarningCategories =
    [
        LedgerCategory.TradeSell,
        LedgerCategory.TradeBuy,
        LedgerCategory.MiningSell,
        LedgerCategory.ContractPayout,
        LedgerCategory.ContractDeposit,
        LedgerCategory.ChartReward,
        LedgerCategory.FuelPurchase,
        LedgerCategory.AntimatterPurchase,
        LedgerCategory.Repair,
    ];

    /// <summary>The ships that earn nothing themselves: slice 6.71's (D151).</summary>
    private static readonly HashSet<string> InfoShips = new(StringComparer.Ordinal)
    {
        "SHIP_PROBE",
        "SHIP_INTERCEPTOR",
        "SHIP_SURVEYOR",
    };

    /// <summary>The drones: what they gather is sold by the shuttle, so their own record shows none of it.</summary>
    private static readonly Dictionary<string, FleetRole> Gatherers = new(StringComparer.Ordinal)
    {
        ["SHIP_MINING_DRONE"] = FleetRole.Mine,
        ["SHIP_ORE_HOUND"] = FleetRole.Mine,
        ["SHIP_SIPHON_DRONE"] = FleetRole.Siphon,
    };

    private const string RecordsKey = "ship-payback:records";
    private const int MaxPurchases = 10_000;

    /// <summary>The role a ship of the type is bought for: a drone's gathering, else trading.</summary>
    /// <param name="shipType">The ship's type, such as <c>SHIP_MINING_DRONE</c>.</param>
    /// <returns>Its role.</returns>
    public static FleetRole RoleOf(string shipType)
        => Gatherers.GetValueOrDefault(shipType.ToUpperInvariant(), FleetRole.Trade);

    /// <summary>Judges whether a ship of the type, bought now at the price, earns its price back before the reset.</summary>
    /// <param name="shipType">The ship's type, such as <c>SHIP_BULK_FREIGHTER</c>.</param>
    /// <param name="price">What it costs.</param>
    /// <param name="now">The time of the purchase.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The verdict; one that isn't judged pays back.</returns>
    public Task<ShipPaybackVerdict> JudgeAsync(string shipType, long price, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipType);
        return JudgeTypeAsync(shipType.ToUpperInvariant(), price, now, cancellationToken);
    }

    /// <summary>The judgement of <see cref="JudgeAsync"/>, of the type in capitals.</summary>
    private async Task<ShipPaybackVerdict> JudgeTypeAsync(string type, long price, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reset = await serverReset.NextAfterAsync(now, cancellationToken);
        if (reset == DateTimeOffset.MaxValue)
        {
            return ShipPaybackVerdict.NotJudged(type, price, "no reset known");
        }

        if (InfoShips.Contains(type))
        {
            return ShipPaybackVerdict.NotJudged(type, price, "a ship that earns nothing itself, judged by its plan (D151)");
        }

        var (perHour, basis) = await EarningsAsync(type, now, cancellationToken);
        if (basis.Length == 0)
        {
            return ShipPaybackVerdict.NotJudged(type, price, $"nothing known of what a {type} earns");
        }

        var lead = await LeadHoursAsync(cancellationToken);
        var hoursLeft = (reset - now).TotalHours;
        return new ShipPaybackVerdict
        {
            PaysBack = perHour > 0 && perHour * (hoursLeft - lead) >= price,
            ShipType = type,
            Price = price,
            CreditsPerHour = perHour,
            HoursLeft = hoursLeft,
            LeadHours = lead,
            Basis = basis,
            Reset = reset,
        };
    }

    /// <summary>
    /// Judges a ship that earns nothing itself by what it serves an hour (slice 6.71, D151): a probe by the promise of
    /// the market it would watch, a surveyor as one drone of its system. It pays back when that, from
    /// <c>Purchase.PaybackLeadHours</c> after the purchase until the reset, covers its price. With nothing known of
    /// what it would serve, it only has to reach its work before the reset. The worth is read only when a reset is
    /// known.
    /// </summary>
    /// <param name="shipType">The ship's type, such as <c>SHIP_INTERCEPTOR</c>.</param>
    /// <param name="price">What it costs.</param>
    /// <param name="worth">Reads what it would serve an hour.</param>
    /// <param name="now">The time of the purchase.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The verdict; one that isn't judged pays back.</returns>
    public Task<ShipPaybackVerdict> JudgeWorthAsync(
        string shipType,
        long price,
        Func<CancellationToken, Task<ShipWorth>> worth,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipType);
        ArgumentNullException.ThrowIfNull(worth);
        return JudgeWorthOfTypeAsync(shipType.ToUpperInvariant(), price, worth, now, cancellationToken);
    }

    /// <summary>
    /// What one drone of the system earns an hour by the role board's mining estimate (slice 6.71, D151: "As much as one
    /// drone"): the average of its estimates of the ships it has mining in the system, else of every ship it has mining.
    /// </summary>
    /// <param name="systemSymbol">The system.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The worth; unknown when the board has no ship mining.</returns>
    public Task<ShipWorth> DroneWorthAsync(string systemSymbol, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(systemSymbol);
        return DroneWorthInAsync(systemSymbol, cancellationToken);
    }

    private async Task<ShipWorth> DroneWorthInAsync(string systemSymbol, CancellationToken cancellationToken)
    {
        var board = await plans.GetAsync<RolePlanState>(PlanTypes.Roles, cancellationToken);
        if (board is null)
        {
            return ShipWorth.Unknown;
        }

        var systems = (await ships.GetAllAsync(cancellationToken))
            .ToDictionary(ship => ship.Symbol, ship => ship.SystemSymbol ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var miners = board.Ships
            .Where(ship => ship.Role == FleetRole.Mine)
            .SelectMany(ship => ship.Estimates
                .Where(estimate => estimate.Role == FleetRole.Mine)
                .Take(1)
                .Select(estimate => (System: systems.GetValueOrDefault(ship.ShipSymbol, string.Empty), estimate.CreditsPerHour)))
            .ToList();
        var here = miners.Where(miner => miner.System.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase)).ToList();
        var chosen = here.Count > 0 ? here : miners;
        return chosen.Count > 0 ? ShipWorth.Of(chosen.Average(miner => (double)miner.CreditsPerHour), FromDrone) : ShipWorth.Unknown;
    }

    /// <summary>
    /// Tells that a plan's need won't pay back: at Information the first time in <see cref="TellEvery"/> for the plan and the
    /// type, at Debug otherwise, as a plan says its need at every pass.
    /// </summary>
    /// <param name="plan">The plan whose need it is.</param>
    /// <param name="verdict">The verdict.</param>
    public void Tell(AutomationPlan plan, ShipPaybackVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        var key = $"ship-payback:told:{plan}:{verdict.ShipType}";
        var level = cache.TryGetValue(key, out _) ? LogLevel.Debug : LogLevel.Information;
        cache.Set(key, true, TellEvery);
        logger.Log(
            level,
            "Ship purchase: the {Plan} plan's {ShipType} for {Price} wouldn't earn its price back before the reset at {Reset}: {CreditsPerHour} an hour by its {Basis}, {PaybackHours} hours with the first {LeadHours}, {HoursLeft} left ({Decision}).",
            plan,
            verdict.ShipType,
            verdict.Price,
            verdict.Reset,
            Math.Round(verdict.CreditsPerHour),
            verdict.Basis,
            Math.Round(verdict.PaybackHours, 1),
            verdict.LeadHours,
            Math.Round(verdict.HoursLeft, 1),
            verdict.Decision);
    }

    private async Task<ShipPaybackVerdict> JudgeWorthOfTypeAsync(
        string type,
        long price,
        Func<CancellationToken, Task<ShipWorth>> worth,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reset = await serverReset.NextAfterAsync(now, cancellationToken);
        if (reset == DateTimeOffset.MaxValue)
        {
            return ShipPaybackVerdict.NotJudged(type, price, "no reset known");
        }

        var lead = await LeadHoursAsync(cancellationToken);
        var hoursLeft = (reset - now).TotalHours;
        var serves = await worth(cancellationToken);
        if (!serves.Known && hoursLeft > lead)
        {
            return ShipPaybackVerdict.NotJudged(type, price, "nothing known of what it would serve, and it reaches its work before the reset");
        }

        return new ShipPaybackVerdict
        {
            PaysBack = serves.Known && serves.PerHour > 0 && serves.PerHour * (hoursLeft - lead) >= price,
            ShipType = type,
            Price = price,
            CreditsPerHour = serves.Known ? serves.PerHour : 0,
            HoursLeft = hoursLeft,
            LeadHours = lead,
            Basis = serves.Known ? serves.Basis : FromArrival,
            Reset = reset,
            Decision = "D151",
        };
    }

    /// <summary>What a ship of the type earns an hour, and by what: its type's record, else the role board's estimate.</summary>
    private async Task<(double PerHour, string Basis)> EarningsAsync(string type, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!Gatherers.ContainsKey(type)
            && (await RecordsAsync(now, cancellationToken)).TryGetValue(type, out var record)
            && record.ShipHours >= MinShipHours)
        {
            return (record.Net / record.ShipHours, FromRecord);
        }

        var estimate = await EstimateAsync(type, RoleOf(type), cancellationToken);
        return estimate.Known ? (estimate.PerHour, FromBoard) : (0, string.Empty);
    }

    /// <summary>Each type's record in the last <see cref="Window"/>, kept for <see cref="RecordsLifetime"/>.</summary>
    private async Task<IReadOnlyDictionary<string, ShipTypeRecord>> RecordsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var records = await cache.GetOrCreateAsync(
            RecordsKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = RecordsLifetime;
                return await WorkOutRecordsAsync(now, cancellationToken);
            });
        return records ?? new Dictionary<string, ShipTypeRecord>(StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, ShipTypeRecord>> WorkOutRecordsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var from = now - Window;
        var fleet = await ships.GetAllAsync(cancellationToken);
        var net = await ledger.GetNetByShipAsync(from, EarningCategories, cancellationToken);
        var purchases = await ledger.GetRangeAsync(from: from, category: LedgerCategory.ShipPurchase, limit: MaxPurchases, cancellationToken: cancellationToken);
        var boughtAt = purchases
            .GroupBy(purchase => purchase.ShipSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(purchase => purchase.OccurredAt), StringComparer.OrdinalIgnoreCase);

        return ShipTypeRecord.Of(fleet, net, boughtAt, from, now);
    }

    /// <summary>
    /// The role board's estimate for the role: the average of its last estimates of the ships of the type, else of every ship
    /// with an estimate for it. Unknown when the board has none.
    /// </summary>
    private async Task<(bool Known, double PerHour)> EstimateAsync(string type, FleetRole role, CancellationToken cancellationToken)
    {
        var board = await plans.GetAsync<RolePlanState>(PlanTypes.Roles, cancellationToken);
        if (board is null)
        {
            return (false, 0);
        }

        var types = (await ships.GetAllAsync(cancellationToken))
            .ToDictionary(ship => ship.Symbol, ShipNames.TypeOf, StringComparer.OrdinalIgnoreCase);
        var estimates = board.Ships
            .SelectMany(ship => ship.Estimates
                .Where(estimate => estimate.Role == role)
                .Take(1)
                .Select(estimate => (Type: types.GetValueOrDefault(ship.ShipSymbol, string.Empty), estimate.CreditsPerHour)))
            .ToList();
        var ofType = estimates.Where(estimate => estimate.Type.Equals(type, StringComparison.Ordinal)).ToList();
        var chosen = ofType.Count > 0 ? ofType : estimates;
        return chosen.Count > 0 ? (true, chosen.Average(estimate => (double)estimate.CreditsPerHour)) : (false, 0);
    }

    private async Task<double> LeadHoursAsync(CancellationToken cancellationToken)
    {
        var stored = await settings.GetRawAsync(LeadSetting, cancellationToken);
        return int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
            ? Math.Max(0, hours)
            : DefaultLeadHours;
    }
}

/// <summary>What the ships of one type earned in a window (<see cref="ShipPayback"/>).</summary>
public sealed record ShipTypeRecord
{
    /// <summary>How many of the type the fleet has.</summary>
    public required int Ships { get; init; }

    /// <summary>The hours they were owned in the window, together.</summary>
    public required double ShipHours { get; init; }

    /// <summary>What they earned in it, net.</summary>
    public required long Net { get; init; }

    /// <summary>Each type's record: its ships' hours since the window's start or their purchase, and their net in the window.</summary>
    /// <param name="fleet">Every ship of the agent.</param>
    /// <param name="net">What each ship earned net in the window, by symbol.</param>
    /// <param name="boughtAt">When the ships bought in the window were bought, by symbol.</param>
    /// <param name="from">The window's start.</param>
    /// <param name="now">Its end.</param>
    /// <returns>The records, by type.</returns>
    public static IReadOnlyDictionary<string, ShipTypeRecord> Of(
        IEnumerable<ShipModel> fleet,
        IReadOnlyDictionary<string, long> net,
        IReadOnlyDictionary<string, DateTimeOffset> boughtAt,
        DateTimeOffset from,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(net);
        ArgumentNullException.ThrowIfNull(boughtAt);

        return fleet
            .Select(ship => (Type: ShipNames.TypeOf(ship), ship.Symbol))
            .Where(ship => ship.Type.Length > 0)
            .GroupBy(ship => ship.Type, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new ShipTypeRecord
                {
                    Ships = group.Count(),
                    ShipHours = group.Sum(ship => Math.Max(0, (now - Later(from, boughtAt.GetValueOrDefault(ship.Symbol, from))).TotalHours)),
                    Net = group.Sum(ship => net.GetValueOrDefault(ship.Symbol)),
                },
                StringComparer.Ordinal);
    }

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;
}

/// <summary>Whether a ship bought now earns its price back before the reset (<see cref="ShipPayback"/>).</summary>
public sealed record ShipPaybackVerdict
{
    /// <summary>Whether it does; true for a purchase that isn't judged.</summary>
    public required bool PaysBack { get; init; }

    /// <summary>The ship's type.</summary>
    public required string ShipType { get; init; }

    /// <summary>Its price.</summary>
    public required long Price { get; init; }

    /// <summary>What a ship of its type earns an hour.</summary>
    public double CreditsPerHour { get; init; }

    /// <summary>The hours to the reset.</summary>
    public double HoursLeft { get; init; }

    /// <summary>The first hours, in which it earns nothing.</summary>
    public double LeadHours { get; init; }

    /// <summary>What the earnings came from (<see cref="ShipPayback.FromRecord"/>, <see cref="ShipPayback.FromBoard"/>); empty when not judged.</summary>
    public string Basis { get; init; } = string.Empty;

    /// <summary>Why it isn't judged; empty when it is.</summary>
    public string NotJudgedBecause { get; init; } = string.Empty;

    /// <summary>The reset it was judged against; <see cref="DateTimeOffset.MaxValue"/> when none is known.</summary>
    public DateTimeOffset Reset { get; init; } = DateTimeOffset.MaxValue;

    /// <summary>
    /// The decision it was judged by: D150 for a ship that earns, D151 for one judged by what it serves.
    /// </summary>
    public string Decision { get; init; } = "D150";

    /// <summary>Whether it was judged.</summary>
    public bool Judged => Basis.Length > 0;

    /// <summary>The hours it takes to earn its price, with the first hours; infinite for a type that earns nothing.</summary>
    public double PaybackHours => CreditsPerHour > 0 ? LeadHours + (Price / CreditsPerHour) : double.PositiveInfinity;

    /// <summary>A verdict on a purchase that isn't judged, which may go.</summary>
    /// <param name="shipType">The ship's type.</param>
    /// <param name="price">Its price.</param>
    /// <param name="because">Why.</param>
    /// <returns>The verdict.</returns>
    public static ShipPaybackVerdict NotJudged(string shipType, long price, string because)
        => new() { PaysBack = true, ShipType = shipType, Price = price, NotJudgedBecause = because };

    /// <inheritdoc />
    public override string ToString()
        => (Judged, Basis) switch
        {
            (false, _) => $"a {ShipType} isn't judged: {NotJudgedBecause}",
            (true, ShipPayback.FromArrival) => string.Create(
                CultureInfo.InvariantCulture,
                $"a {ShipType} for {Price} can't reach its work before the reset at {Reset:u}: {HoursLeft:F1} hours left, its first {LeadHours} on its way ({Decision})"),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"a {ShipType} for {Price} earns {CreditsPerHour:F0} an hour by its {Basis}: {PaybackHours:F1} hours with the first {LeadHours}, {HoursLeft:F1} left before the reset at {Reset:u} ({Decision})"),
        };
}

/// <summary>
/// What a ship that earns nothing itself serves an hour (<see cref="ShipPayback.JudgeWorthAsync"/>, D151).
/// </summary>
public sealed record ShipWorth
{
    /// <summary>Nothing is known of it.</summary>
    public static readonly ShipWorth Unknown = new() { Known = false };

    /// <summary>Whether it is known.</summary>
    public required bool Known { get; init; }

    /// <summary>What it serves an hour.</summary>
    public double PerHour { get; init; }

    /// <summary>What that is: <see cref="Probes.ProbeWorth.Basis"/>, <see cref="ShipPayback.FromDrone"/>.</summary>
    public string Basis { get; init; } = string.Empty;

    /// <summary>A known worth.</summary>
    /// <param name="perHour">What it serves an hour.</param>
    /// <param name="basis">What that is.</param>
    /// <returns>The worth.</returns>
    public static ShipWorth Of(double perHour, string basis) => new() { Known = true, PerHour = perHour, Basis = basis };
}
