using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Roles;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>For the plans' tests: the role board switched on, with the roles it gave (slice 6.9).</summary>
internal static class RoleBoardTestSupport
{
    /// <summary>Switches the role board on, and has the plan state hold these roles.</summary>
    /// <param name="settings">The settings substitute.</param>
    /// <param name="plans">The plan states substitute.</param>
    /// <param name="roles">Each ship's role on the board.</param>
    public static void RolesAre(ISettingsRepository settings, IPlanRepository plans, params (string Ship, FleetRole Role)[] roles)
    {
        settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Roles), Arg.Any<CancellationToken>()).Returns(true);
        plans.GetAsync<RolePlanState>(PlanTypes.Roles, Arg.Any<CancellationToken>()).Returns(new RolePlanState
        {
            EvaluatedAt = DateTimeOffset.UtcNow,
            Ships = [.. roles.Select(entry => new RoleShipState { ShipSymbol = entry.Ship, Role = entry.Role, Reason = "test", Since = DateTimeOffset.UtcNow })],
        });
    }
}
