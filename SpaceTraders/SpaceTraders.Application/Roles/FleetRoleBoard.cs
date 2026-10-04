using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Roles;

/// <summary>
/// Which plan a ship works for, as every plan reads it in one pass (PLAN.md slice 6.9). With the role board on
/// (<c>Automation.Plan.Roles.Enabled</c>), from the role each ship has on the board:
/// <list type="bullet">
///   <item>a ship with the survey role surveys, and with the spare-time plan on trades or gathers when it has nothing
///   to survey (D34);</item>
///   <item>a ship with the mining role mines, and one with the siphon role siphons; each trades when its own plan has
///   no trip for it, as before;</item>
///   <item>a ship with the trade role trades;</item>
///   <item>a ship with the construction role builds the jump gate (slice 6.6), and trades when the construction plan has
///   nothing it may buy;</item>
///   <item>the contract takes every ship that can mine but the one that surveys (D40), whatever its role, so the
///   contract never waits for the next evaluation;</item>
///   <item>a ship the board hasn't seen yet, bought this tick, waits for its role: one tick.</item>
/// </list>
/// With the board off, by <see cref="FleetRoles"/>' fixed rules (D20, D34), as before slice 6.9.
/// </summary>
public sealed class FleetRoleBoard
{
    private readonly IReadOnlyDictionary<string, FleetRole> _roles;

    private FleetRoleBoard(bool rolesOn, bool surveyOn, bool spareTimeOn, IReadOnlyDictionary<string, FleetRole> roles)
    {
        RolesOn = rolesOn;
        SurveyOn = surveyOn;
        SpareTimeOn = spareTimeOn;
        _roles = roles;
    }

    /// <summary>Whether the role board gives the roles; otherwise the fixed rules do.</summary>
    public bool RolesOn { get; }

    /// <summary>Whether the survey plan is on.</summary>
    public bool SurveyOn { get; }

    /// <summary>Whether the spare-time plan is on.</summary>
    public bool SpareTimeOn { get; }

    /// <summary>Reads the switches and, with the board on, the roles it gave.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="plans">The plan states.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <param name="surveyOn">Whether the survey plan is on, when the caller knows: the survey plan runs only when it is.</param>
    /// <param name="rolesOn">Whether the role board is on, when the caller knows: the health rules judge by their own switches.</param>
    /// <returns>The board for this pass.</returns>
    public static Task<FleetRoleBoard> ReadAsync(
        ISettingsRepository settings,
        IPlanRepository plans,
        CancellationToken cancellationToken,
        bool? surveyOn = null,
        bool? rolesOn = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(plans);
        return ReadBoardAsync(settings, plans, surveyOn, rolesOn, cancellationToken);
    }

    private static async Task<FleetRoleBoard> ReadBoardAsync(
        ISettingsRepository settings,
        IPlanRepository plans,
        bool? knownSurveyOn,
        bool? knownRolesOn,
        CancellationToken cancellationToken)
    {
        var rolesOn = knownRolesOn ?? await settings.IsPlanEnabledAsync(AutomationPlan.Roles, cancellationToken);
        var surveyOn = knownSurveyOn ?? await settings.IsPlanEnabledAsync(AutomationPlan.Survey, cancellationToken);
        var spareTimeOn = await settings.IsPlanEnabledAsync(AutomationPlan.SpareTime, cancellationToken);
        var roles = rolesOn && await plans.GetAsync<RolePlanState>(PlanTypes.Roles, cancellationToken) is { } state
            ? state.Ships
                .GroupBy(ship => ship.ShipSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Role, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, FleetRole>(StringComparer.OrdinalIgnoreCase);
        return new FleetRoleBoard(rolesOn, surveyOn, spareTimeOn, roles);
    }

    /// <summary>A board for the fixed rules or given roles, for tests.</summary>
    /// <param name="rolesOn">Whether the role board gives the roles.</param>
    /// <param name="surveyOn">Whether the survey plan is on.</param>
    /// <param name="spareTimeOn">Whether the spare-time plan is on.</param>
    /// <param name="roles">The roles by ship, with the board on.</param>
    /// <returns>The board.</returns>
    public static FleetRoleBoard For(bool rolesOn, bool surveyOn, bool spareTimeOn, IReadOnlyDictionary<string, FleetRole>? roles = null)
        => new(rolesOn, surveyOn, spareTimeOn, new Dictionary<string, FleetRole>(roles ?? new Dictionary<string, FleetRole>(), StringComparer.OrdinalIgnoreCase));

    /// <summary>The ship's role on the board; <see cref="FleetRole.None"/> with the board off, or for a ship it hasn't seen.</summary>
    /// <param name="ship">The ship.</param>
    /// <returns>Its role.</returns>
    public FleetRole RoleOf(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return _roles.GetValueOrDefault(ship.Symbol, FleetRole.None);
    }

    /// <summary>Whether the ship surveys: the survey role (the board), or a surveyor while the survey plan is on (D20).</summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship the survey plan gives surveys.</returns>
    public bool IsSurveyor(ShipModel ship)
        => RolesOn
            ? SurveyOn && RoleOf(ship) == FleetRole.Survey && FleetRoles.CanSurvey(ship)
            : FleetRoles.IsSurveyor(ship, SurveyOn);

    /// <summary>Whether the ship mines for the mining plan: the mining role (the board), or a miner that doesn't survey (D20).</summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship the mining plan gives trips.</returns>
    public bool IsMiner(ShipModel ship)
        => RolesOn
            ? RoleOf(ship) == FleetRole.Mine && FleetRoles.CanMine(ship)
            : FleetRoles.IsMiner(ship, SurveyOn);

    /// <summary>
    /// Whether the contract may take the ship (D23, D40): it can mine and doesn't survey. With the board on, whatever its
    /// role, so the contract doesn't wait for the next evaluation, which then gives it the mining role.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship the contract plan may give contract work.</returns>
    public bool MinesForContract(ShipModel ship)
        => RolesOn
            ? FleetRoles.CanMine(ship) && !IsSurveyor(ship)
            : FleetRoles.IsMiner(ship, SurveyOn);

    /// <summary>Whether the ship siphons for the siphon plan: the siphon role (the board), or a siphon drone (slice 6.7).</summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship the siphon plan gives trips.</returns>
    public bool IsSiphoner(ShipModel ship)
        => RolesOn
            ? RoleOf(ship) == FleetRole.Siphon && FleetRoles.CanSiphon(ship)
            : FleetRoles.IsSiphoner(ship);

    /// <summary>
    /// Whether the ship builds the jump gate for the construction plan (slice 6.6, D65): the construction role (the board).
    /// With the board off the construction plan picks its builders by the same rule, the largest holds.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship the construction plan gives trips, with the board on.</returns>
    public bool IsBuilder(ShipModel ship)
        => RolesOn && RoleOf(ship) == FleetRole.Construct && FleetRoles.CanConstruct(ship);

    /// <summary>
    /// Whether the trading plan may give the ship a route: a hold and a tank, and the trade role, or the mining, siphon or
    /// construction role when that plan had no trip for it (the board); with the board off, any such ship that doesn't
    /// survey (D20). A ship that gathers in its spare time trades by its own rule (D34).
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship the trading plan may treat as a trader.</returns>
    public bool IsTrader(ShipModel ship)
        => ship.IsTradingCapable
            && (RolesOn
                ? RoleOf(ship) is FleetRole.Trade or FleetRole.Mine or FleetRole.Siphon or FleetRole.Construct
                : !FleetRoles.IsSurveyor(ship, SurveyOn));

    /// <summary>
    /// Whether the ship mines or siphons in its spare time (slice 6.8): it surveys (<see cref="IsSurveyor"/>), and has a
    /// hold, a tank, and a mining laser or a gas siphon. The spare-time plan must be on for it to do so.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship the spare-time plan may give a trip.</returns>
    public bool GathersInSpareTime(ShipModel ship)
        => RolesOn
            ? IsSurveyor(ship) && ship.IsTradingCapable && (FleetRoles.HasMiningLaser(ship) || ship.HasGasSiphonEquipment)
            : FleetRoles.GathersInSpareTime(ship, SurveyOn);
}
