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

    /// <summary>The role earns the fleet the most per hour (D38).</summary>
    public const string MostProfitable = "most_profitable";

    /// <summary>None of the ship's roles has a trip for it; it keeps its role.</summary>
    public const string NoWork = "no_work";

    /// <summary>The ship has no role whose plan is on, or is a probe.</summary>
    public const string NoRole = "no_role";

    /// <summary>The most trips per ship and role the assignment weighs: enough for every ship to find one, within reason.</summary>
    public const int MaxOptionsPerRole = 20;

    /// <summary>Decides every ship's role.</summary>
    /// <param name="ships">The fleet, but for its probes.</param>
    /// <param name="contractWantsOre">Whether the contract plan is on and its contract still wants ore (D40).</param>
    /// <param name="headStart">How much more a ship's current role counts: 0.2 for 20% (D41).</param>
    /// <returns>A decision per ship, by symbol.</returns>
    public static IReadOnlyList<RoleDecision> Decide(IReadOnlyList<RoleCandidate> ships, bool contractWantsOre, double headStart)
    {
        ArgumentNullException.ThrowIfNull(ships);

        var bonus = 1 + Math.Max(0, headStart);
        var decisions = new Dictionary<string, RoleDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (var ship in ships.Where(ship => ship.Roles.Count <= 1))
        {
            decisions[ship.Ship.Symbol] = ship.Roles.Count == 0
                ? new RoleDecision(ship.Ship.Symbol, FleetRole.None, NoRole, null)
                : new RoleDecision(ship.Ship.Symbol, ship.Roles[0], OnlyRole, null);
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

    /// <summary>The role a ship without a trip keeps: its own, when it still has it and it isn't surveying; else its first role but surveying.</summary>
    private static FleetRole Keep(RoleCandidate ship)
        => ship.Current != FleetRole.Survey && ship.Roles.Contains(ship.Current)
            ? ship.Current
            : ship.Roles.FirstOrDefault(role => role != FleetRole.Survey);
}
