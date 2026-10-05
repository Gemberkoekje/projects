using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Roles;

/// <summary>One ship as the role board weighs it (PLAN.md slice 6.9).</summary>
public sealed record RoleCandidate
{
    /// <summary>Creates a candidate.</summary>
    /// <param name="Ship">The ship.</param>
    /// <param name="Roles">The roles it could take whose plan is on, in the order survey, mine, siphon, trade.</param>
    /// <param name="Current">Its role now; <see cref="FleetRole.None"/> for a ship the board hasn't seen.</param>
    /// <param name="Options">Its best trips in those roles, from <see cref="RoleEstimator.Options"/>; none for surveying.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RoleCandidate(ShipModel Ship, IReadOnlyList<FleetRole> Roles, FleetRole Current, IReadOnlyList<RoleOption> Options)
    {
        this.Ship = Ship;
        this.Roles = Roles;
        this.Current = Current;
        this.Options = Options;
    }

    /// <summary>The ship.</summary>
    public required ShipModel Ship { get; init; }

    /// <summary>The roles it could take whose plan is on.</summary>
    public required IReadOnlyList<FleetRole> Roles { get; init; }

    /// <summary>Its role now; <see cref="FleetRole.None"/> for a ship the board hasn't seen.</summary>
    public required FleetRole Current { get; init; }

    /// <summary>Its best trips in those roles; none for surveying.</summary>
    public required IReadOnlyList<RoleOption> Options { get; init; }

    /// <summary>What its best trip outside surveying earns per hour; 0 without one.</summary>
    public double BestPerHour => Options.Where(option => option.Role != FleetRole.Survey).Select(option => option.CreditsPerHour).DefaultIfEmpty(0).Max();
}

/// <summary>A ship's role, why, and the trip that decided it, when one did.</summary>
public sealed record RoleDecision
{
    /// <summary>Creates a decision.</summary>
    /// <param name="ShipSymbol">The ship.</param>
    /// <param name="Role">Its role.</param>
    /// <param name="Reason">Why (<see cref="RolePlanner"/>'s reasons).</param>
    /// <param name="Option">The trip that decided it: for <see cref="RolePlanner.MostProfitable"/>; else none.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RoleDecision(string ShipSymbol, FleetRole Role, string Reason, RoleOption? Option)
    {
        this.ShipSymbol = ShipSymbol;
        this.Role = Role;
        this.Reason = Reason;
        this.Option = Option;
    }

    /// <summary>The ship.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>Its role.</summary>
    public required FleetRole Role { get; init; }

    /// <summary>Why it has it.</summary>
    public required string Reason { get; init; }

    /// <summary>The trip that decided it, for a role chosen by profit.</summary>
    public RoleOption? Option { get; init; }
}

/// <summary>
/// A SCARCE or LIMITED mineral, an ore or a gas, in one area, as the role board keeps a drone gathering it (PLAN.md slice
/// 6.10b, D48, D53).
/// </summary>
public sealed record MineralCoverage
{
    /// <summary>Creates a mineral to keep a drone for.</summary>
    /// <param name="Good">The ore or gas.</param>
    /// <param name="Role">The role that gathers it: <see cref="FleetRole.Mine"/> or <see cref="FleetRole.Siphon"/>.</param>
    /// <param name="AbleShipSymbols">The drones that could serve a market short of it.</param>
    /// <param name="WorkingShipSymbols">The ships whose trip covers it now.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MineralCoverage(string Good, FleetRole Role, IReadOnlyList<string> AbleShipSymbols, IReadOnlyList<string> WorkingShipSymbols)
    {
        this.Good = Good;
        this.Role = Role;
        this.AbleShipSymbols = AbleShipSymbols;
        this.WorkingShipSymbols = WorkingShipSymbols;
    }

    /// <summary>The ore or gas.</summary>
    public required string Good { get; init; }

    /// <summary>The role that gathers it.</summary>
    public required FleetRole Role { get; init; }

    /// <summary>The drones that could serve a market short of it.</summary>
    public required IReadOnlyList<string> AbleShipSymbols { get; init; }

    /// <summary>The ships whose trip covers it now.</summary>
    public required IReadOnlyList<string> WorkingShipSymbols { get; init; }

    /// <summary>The area's markets short of it, by symbol (D53): none where the area doesn't matter.</summary>
    public IReadOnlyList<string> MarketSymbols { get; init; } = [];
}

/// <summary>
/// Which role each ship takes (PLAN.md slice 6.9), without any I/O, so the board and its tests decide alike. Asked on
/// 2026-10-02: "If there is only 1 ship that can survey, then that ship should prioritize surveying. But if there are 2
/// ships that can survey, but one of them can only survey and the other can survey, mine, trade and siphon, the ship
/// that can only survey should take the job." In order:
/// <list type="number">
///   <item>a ship with one role takes it (<see cref="OnlyRole"/>): a survey ship surveys, a hauler trades;</item>
///   <item>surveys come first (D38): in a system with a ship that can only survey, it surveys and the others don't;
///   otherwise, while another ship there can mine (surveys are for miners), the ship that can survey with the least
///   to lose surveys (<see cref="SurveyFirst"/>): the one whose best other trip earns least per hour. The one that
///   surveys now keeps it unless another would lose less by more than the head start;</item>
///   <item>while the contract wants ore (D40, D23 kept), every other ship that can mine mines (<see cref="Contract"/>);</item>
///   <item>one drone per SCARCE or LIMITED mineral and area keeps gathering it (<see cref="Coverage"/>, D48, D53): the
///   drone whose trip covers it, else one that has the role, else the one with the least to lose. Without that, a drone
///   that earns more trading would leave its mineral, and the plan would buy the next;</item>
///   <item>every other drone gathers too (<see cref="GathersFirst"/>, D58): a drone, which can mine or siphon and trade and
///   nothing else, takes its gathering role whatever trading would pay, and trades only when its plan has no trip for it.
///   Moved to trading for profit, drones left the minerals they had mined short, and the plans bought drones for them;</item>
///   <item>while a system's jump gate needs materials, the ships with the largest holds there, as many as
///   <c>Construction.Ships</c> (one), build it (<see cref="Construction"/>, slice 6.6, D65): supplying pays nothing, so no
///   estimate could choose it, and finishing the gate comes first. Drones and the ship that surveys are decided by then.
///   Of two equal holds, the one that builds now keeps it, else the one that can do least else;</item>
///   <item>the rest share the work for the most credits per hour across the fleet (<see cref="MostProfitable"/>): each
///   takes one trip, and no two the same trade route (D18) or the same mining or siphon opening. A ship's current role
///   counts the head start more (D41), so a close call doesn't flip back and forth. A ship left without a trip keeps
///   its role (<see cref="NoWork"/>).</item>
/// </list>
/// </summary>
public static class RolePlanner
{
    /// <summary>The ship has one role whose plan is on.</summary>
    public const string OnlyRole = "only_role";

    /// <summary>The ship surveys: it can, and of those that can, it has the least to lose (D38).</summary>
    public const string SurveyFirst = "survey_first";

    /// <summary>The ship mines for the contract, which comes first (D40).</summary>
    public const string Contract = "contract";

    /// <summary>The drone keeps gathering a SCARCE or LIMITED mineral, one drone each per area (D48, D53).</summary>
    public const string Coverage = "coverage";

    /// <summary>
    /// The drone gathers, whatever trading would pay (D58): "Mining drones should be mining drones first, and traders
    /// second". It trades only when its plan has no trip for it.
    /// </summary>
    public const string GathersFirst = "gathers_first";

    /// <summary>The ship builds the jump gate: of those that can, it has the largest hold (slice 6.6, D65).</summary>
    public const string Construction = "construction";

    /// <summary>
    /// The shuttle collects at the far asteroid the mining plan designated it for, where a drone is parked (slice 6.18, D83,
    /// D86).
    /// </summary>
    public const string Collection = "collection";

    /// <summary>The role earns the fleet the most per hour (D38).</summary>
    public const string MostProfitable = "most_profitable";

    /// <summary>None of the ship's roles has a trip for it; it keeps its role.</summary>
    public const string NoWork = "no_work";

    /// <summary>The ship has no role whose plan is on, or is a probe.</summary>
    public const string NoRole = "no_role";

    /// <summary>The most trips per ship and role the assignment weighs: enough for every ship to find one, within reason.</summary>
    public const int MaxOptionsPerRole = 20;

    /// <summary>Decides every ship's role, with no mineral to keep a drone for.</summary>
    /// <param name="ships">The fleet, but for its probes.</param>
    /// <param name="contractWantsOre">Whether the contract plan is on and its contract still wants ore (D40).</param>
    /// <param name="headStart">How much more a ship's current role counts: 0.2 for 20% (D41).</param>
    /// <returns>A decision per ship, by symbol.</returns>
    public static IReadOnlyList<RoleDecision> Decide(IReadOnlyList<RoleCandidate> ships, bool contractWantsOre, double headStart)
        => Decide(ships, contractWantsOre, headStart, []);

    /// <summary>Decides every ship's role.</summary>
    /// <param name="ships">The fleet, but for its probes.</param>
    /// <param name="contractWantsOre">Whether the contract plan is on and its contract still wants ore (D40).</param>
    /// <param name="headStart">How much more a ship's current role counts: 0.2 for 20% (D41).</param>
    /// <param name="coverage">The SCARCE or LIMITED minerals, each to keep a drone gathering (D48).</param>
    /// <param name="builders">
    /// How many ships per system build its jump gate (<c>Construction.Ships</c>, D65), of those that have the construction
    /// role available: only where the gate needs materials.
    /// </param>
    /// <returns>A decision per ship, by symbol.</returns>
    public static IReadOnlyList<RoleDecision> Decide(
        IReadOnlyList<RoleCandidate> ships,
        bool contractWantsOre,
        double headStart,
        IReadOnlyList<MineralCoverage> coverage,
        int builders = 1)
        => Decide(ships, contractWantsOre, headStart, coverage, new HashSet<string>(StringComparer.OrdinalIgnoreCase), builders);

    /// <summary>Decides every ship's role, the shuttles the mining plan designated keeping the collecting role.</summary>
    /// <param name="ships">The fleet, but for its probes.</param>
    /// <param name="contractWantsOre">Whether the contract plan is on and its contract still wants ore (D40).</param>
    /// <param name="headStart">How much more a ship's current role counts: 0.2 for 20% (D41).</param>
    /// <param name="coverage">The SCARCE or LIMITED minerals, each to keep a drone gathering (D48).</param>
    /// <param name="collectors">
    /// The shuttles the mining plan designated to collect at a far asteroid where a drone is parked (slice 6.18, D83, D86),
    /// by symbol: each keeps the collecting role, whatever else it could do.
    /// </param>
    /// <param name="builders">
    /// How many ships per system build its jump gate (<c>Construction.Ships</c>, D65), of those that have the construction
    /// role available: only where the gate needs materials.
    /// </param>
    /// <returns>A decision per ship, by symbol.</returns>
    public static IReadOnlyList<RoleDecision> Decide(
        IReadOnlyList<RoleCandidate> ships,
        bool contractWantsOre,
        double headStart,
        IReadOnlyList<MineralCoverage> coverage,
        IReadOnlySet<string> collectors,
        int builders = 1)
    {
        ArgumentNullException.ThrowIfNull(ships);
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(collectors);

        var bonus = 1 + Math.Max(0, headStart);
        var decisions = new Dictionary<string, RoleDecision>(StringComparer.OrdinalIgnoreCase);

        // The construction role goes only to the ships with the largest holds (D65), even where it is a ship's one role.
        foreach (var ship in ships.Where(ship => ship.Roles.Count <= 1 && !ship.Roles.Contains(FleetRole.Construct)))
        {
            decisions[ship.Ship.Symbol] = ship.Roles.Count == 0
                ? new RoleDecision(ship.Ship.Symbol, FleetRole.None, NoRole, null)
                : new RoleDecision(ship.Ship.Symbol, ship.Roles[0], OnlyRole, null);
        }

        // D83: a shuttle the mining plan designated for a far asteroid collects there, and does nothing else.
        foreach (var collector in ships.Where(ship => collectors.Contains(ship.Ship.Symbol) && FleetRoles.IsCargoShip(ship.Ship)))
        {
            decisions[collector.Ship.Symbol] = new RoleDecision(collector.Ship.Symbol, FleetRole.Collect, Collection, null);
        }

        foreach (var surveyor in SurveyHolders(ships, bonus))
        {
            decisions[surveyor.Ship.Symbol] = new RoleDecision(surveyor.Ship.Symbol, FleetRole.Survey, SurveyFirst, null);
        }

        if (contractWantsOre)
        {
            foreach (var miner in ships.Where(ship => !decisions.ContainsKey(ship.Ship.Symbol) && ship.Roles.Contains(FleetRole.Mine)))
            {
                decisions[miner.Ship.Symbol] = new RoleDecision(miner.Ship.Symbol, FleetRole.Mine, Contract, null);
            }
        }

        foreach (var (keeper, role) in Keepers(ships, coverage, decisions).ToList())
        {
            decisions[keeper.Ship.Symbol] = new RoleDecision(keeper.Ship.Symbol, role, Coverage, null);
        }

        foreach (var drone in ships.Where(ship => !decisions.ContainsKey(ship.Ship.Symbol) && IsDrone(ship)))
        {
            decisions[drone.Ship.Symbol] = new RoleDecision(drone.Ship.Symbol, GatheringRole(drone), GathersFirst, null);
        }

        foreach (var builder in Builders(ships, decisions, builders))
        {
            decisions[builder.Ship.Symbol] = new RoleDecision(builder.Ship.Symbol, FleetRole.Construct, Construction, null);
        }

        foreach (var decision in ShareTheWork([.. ships.Where(ship => !decisions.ContainsKey(ship.Ship.Symbol))], bonus))
        {
            decisions[decision.ShipSymbol] = decision;
        }

        return [.. decisions.Values.OrderBy(decision => decision.ShipSymbol, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The ships with more than one role that survey: per system, none where a ship can only survey, or where no other
    /// ship can mine; else the one with the least to lose, the one surveying now keeping it unless another would lose
    /// less by more than the head start.
    /// </summary>
    private static IEnumerable<RoleCandidate> SurveyHolders(IReadOnlyList<RoleCandidate> ships, double bonus)
    {
        foreach (var system in ships
            .Where(ship => ship.Roles.Contains(FleetRole.Survey))
            .GroupBy(ship => ship.Ship.SystemSymbol ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = system.Where(ship => ship.Roles.Count > 1).OrderBy(ship => ship.Ship.Symbol, StringComparer.Ordinal).ToList();
            if (candidates.Count == 0 || system.Any(ship => ship.Roles.Count == 1))
            {
                continue;
            }

            var withMiners = candidates
                .Where(candidate => ships.Any(other => other != candidate
                    && other.Roles.Contains(FleetRole.Mine)
                    && string.Equals(other.Ship.SystemSymbol, candidate.Ship.SystemSymbol, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (withMiners.Count == 0)
            {
                continue;
            }

            var cheapest = withMiners.MinBy(candidate => candidate.BestPerHour)!;
            var current = withMiners.FirstOrDefault(candidate => candidate.Current == FleetRole.Survey);
            yield return current is not null && current.BestPerHour <= cheapest.BestPerHour * bonus ? current : cheapest;
        }
    }

    /// <summary>
    /// The ships that build each system's jump gate (slice 6.6, D65): of those not yet decided that have the construction
    /// role available (only where the gate needs materials), the largest holds, as many as <paramref name="count"/>; of two
    /// equal holds, the one that builds now, then the one that can do least else (a cargo ship before the command ship), then
    /// by symbol.
    /// </summary>
    private static IEnumerable<RoleCandidate> Builders(
        IReadOnlyList<RoleCandidate> ships,
        IReadOnlyDictionary<string, RoleDecision> decided,
        int count)
        => ships
            .Where(ship => !decided.ContainsKey(ship.Ship.Symbol) && ship.Roles.Contains(FleetRole.Construct))
            .GroupBy(ship => ship.Ship.SystemSymbol ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .SelectMany(system => system
                .OrderByDescending(ship => ship.Ship.CargoCapacity)
                .ThenByDescending(ship => ship.Current == FleetRole.Construct)
                .ThenBy(ship => ship.Roles.Count)
                .ThenBy(ship => ship.Ship.Symbol, StringComparer.Ordinal)
                .Take(Math.Max(0, count)))
            .ToList();

    /// <summary>
    /// Whether the ship is a drone (D58): it can mine or siphon, whichever plan of those is on, and can't survey, so not the
    /// command ship, whatever role that has.
    /// </summary>
    private static bool IsDrone(RoleCandidate ship)
        => !FleetRoles.CanSurvey(ship.Ship) && ship.Roles.Any(role => role is FleetRole.Mine or FleetRole.Siphon);

    /// <summary>A drone's gathering role (D58): mining for a mining drone, siphoning for a siphon drone.</summary>
    private static FleetRole GatheringRole(RoleCandidate drone)
        => drone.Roles.Contains(FleetRole.Mine) ? FleetRole.Mine : FleetRole.Siphon;

    /// <summary>
    /// A drone to keep gathering each SCARCE or LIMITED mineral in each area (D48, D53), one each, the minerals the fewest
    /// drones could serve first: of the drones not yet decided that could serve it and have its role, the one whose trip
    /// covers it, else one that has the role now, else the one with the least to lose (its best trip in another role earns
    /// least per hour). A ship that can survey is no drone: the command ship takes what pays it most.
    /// </summary>
    private static IEnumerable<(RoleCandidate Keeper, FleetRole Role)> Keepers(
        IReadOnlyList<RoleCandidate> ships,
        IReadOnlyList<MineralCoverage> coverage,
        IReadOnlyDictionary<string, RoleDecision> decided)
    {
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mineral in coverage
            .OrderBy(mineral => mineral.AbleShipSymbols.Count)
            .ThenBy(mineral => mineral.Good, StringComparer.Ordinal)
            .ThenBy(mineral => string.Join(',', mineral.MarketSymbols), StringComparer.Ordinal))
        {
            var able = mineral.AbleShipSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var working = mineral.WorkingShipSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keeper = ships
                .Where(ship => able.Contains(ship.Ship.Symbol)
                    && !decided.ContainsKey(ship.Ship.Symbol)
                    && !kept.Contains(ship.Ship.Symbol)
                    && ship.Roles.Contains(mineral.Role)
                    && !ship.Roles.Contains(FleetRole.Survey))
                .OrderByDescending(ship => working.Contains(ship.Ship.Symbol))
                .ThenByDescending(ship => ship.Current == mineral.Role)
                .ThenBy(ship => ship.Options.Where(option => option.Role != mineral.Role && option.Role != FleetRole.Survey).Select(option => option.CreditsPerHour).DefaultIfEmpty(0).Max())
                .ThenBy(ship => ship.Ship.Symbol, StringComparer.Ordinal)
                .FirstOrDefault();
            if (keeper is not null)
            {
                kept.Add(keeper.Ship.Symbol);
                yield return (keeper, mineral.Role);
            }
        }
    }

    /// <summary>
    /// The assignment: each ship one trip (or none), no trip twice, for the most per hour across the fleet; a ship's
    /// current role counts the head start more.
    /// </summary>
    private static IEnumerable<RoleDecision> ShareTheWork(IReadOnlyList<RoleCandidate> ships, double bonus)
    {
        if (ships.Count == 0)
        {
            yield break;
        }

        var options = ships
            .Select(ship => ship.Options.Where(option => option.Role != FleetRole.Survey && ship.Roles.Contains(option.Role)).ToList())
            .ToList();
        var jobs = options
            .SelectMany(list => list.Select(option => option.JobKey))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var column = jobs.Select((key, index) => (key, index)).ToDictionary(entry => entry.key, entry => entry.index, StringComparer.Ordinal);

        // A column per job, and one per ship for no trip at all, worth nothing.
        var values = new double[ships.Count, jobs.Count + ships.Count];
        for (var row = 0; row < ships.Count; row++)
        {
            foreach (var option in options[row])
            {
                var value = option.CreditsPerHour * (option.Role == ships[row].Current ? bonus : 1);
                values[row, column[option.JobKey]] = Math.Max(values[row, column[option.JobKey]], value);
            }
        }

        var assigned = Assignment.Maximize(values);
        for (var row = 0; row < ships.Count; row++)
        {
            var ship = ships[row];
            var chosen = assigned[row] < jobs.Count
                ? options[row].Where(option => option.JobKey == jobs[assigned[row]]).MaxBy(option => option.CreditsPerHour)
                : null;
            yield return chosen is not null
                ? new RoleDecision(ship.Ship.Symbol, chosen.Role, MostProfitable, chosen)
                : new RoleDecision(ship.Ship.Symbol, Keep(ship), NoWork, null);
        }
    }

    /// <summary>
    /// The role a ship without a trip keeps: its own, when it still has it and it isn't surveying or building; else its first
    /// role but those. Surveying and building go only to the ships chosen for them.
    /// </summary>
    private static FleetRole Keep(RoleCandidate ship)
        => ship.Current is not FleetRole.Survey and not FleetRole.Construct && ship.Roles.Contains(ship.Current)
            ? ship.Current
            : ship.Roles.FirstOrDefault(role => role is not FleetRole.Survey and not FleetRole.Construct);
}
