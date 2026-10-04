using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// The plans the tick runs, in this order. Each has its own on/off setting. The role board (slice 6.9) runs before
/// the plans whose ships it gives roles; the construction plan (slice 6.6) before trading, which takes its ship when it
/// has nothing it may buy.
/// </summary>
public enum AutomationPlan
{
    Scout,
    Roles,
    Contract,
    ProbeDeployment,
    Survey,
    Mining,
    Siphon,
    Construction,
    Trading,
    SpareTime,
}

/// <summary>
/// The automation switches: the master switch <c>Automation.Enabled</c>, and one
/// <c>Automation.Plan.{Plan}.Enabled</c> per plan. A missing setting counts as off.
/// </summary>
public static class AutomationSwitches
{
    public const string EnabledSetting = "Automation.Enabled";

    public static string PlanEnabledSetting(AutomationPlan plan) => $"Automation.Plan.{plan}.Enabled";

    public static Task<bool> IsAutomationEnabledAsync(this ISettingsRepository settings, CancellationToken cancellationToken) =>
        settings.GetAsync<bool>(EnabledSetting, cancellationToken);

    public static Task<bool> IsPlanEnabledAsync(this ISettingsRepository settings, AutomationPlan plan, CancellationToken cancellationToken) =>
        settings.GetAsync<bool>(PlanEnabledSetting(plan), cancellationToken);

    /// <summary>The plan that gives ships this kind of goal, or <c>null</c> for goals no plan gives.</summary>
    public static AutomationPlan? PlanFor(ShipGoal goal) => goal switch
    {
        ScoutWaypointGoal => AutomationPlan.Scout,
        DeployProbeGoal => AutomationPlan.ProbeDeployment,
        SurveyWaypointGoal => AutomationPlan.Survey,

        // Only the survey plan moves ships, to where most drones mine (D54, D55).
        MoveToWaypointGoal => AutomationPlan.Survey,
        MineAndSellGoal => AutomationPlan.Mining,
        SiphonAndSellGoal => AutomationPlan.Siphon,
        TradeBetweenMarketsGoal => AutomationPlan.Trading,
        GatherAndSellGoal => AutomationPlan.SpareTime,
        SupplyConstructionGoal => AutomationPlan.Construction,
        _ => null,
    };
}
