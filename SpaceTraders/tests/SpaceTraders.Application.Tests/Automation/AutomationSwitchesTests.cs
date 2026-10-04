using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Automation;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class AutomationSwitchesTests
{
    [Fact]
    public async Task Seed_SwitchesOnAutomation_ButOnlyTheScoutAndContractPlans()
    {
        // D9: the first run after the redeploy runs only the scout and contract plans.
        await using var db = TestDbContextFactory.Create();
        await DefaultSettingsSeed.SeedAsync(db);

        var switches = await db.Settings
            .Where(s => s.Key == "Automation.Enabled" || s.Key.StartsWith("Automation.Plan."))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        switches.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Automation.Enabled"] = "true",
            ["Automation.Plan.Scout.Enabled"] = "true",
            ["Automation.Plan.Explore.Enabled"] = "false",
            ["Automation.Plan.Roles.Enabled"] = "false",
            ["Automation.Plan.Contract.Enabled"] = "true",
            ["Automation.Plan.ProbeDeployment.Enabled"] = "false",
            ["Automation.Plan.Survey.Enabled"] = "false",
            ["Automation.Plan.Mining.Enabled"] = "false",
            ["Automation.Plan.Siphon.Enabled"] = "false",
            ["Automation.Plan.Trading.Enabled"] = "false",
            ["Automation.Plan.SpareTime.Enabled"] = "false",
        });
    }

    [Fact]
    public void EveryPlan_HasTheSwitchTheSeedUses()
    {
        Enum.GetValues<AutomationPlan>()
            .Select(AutomationSwitches.PlanEnabledSetting)
            .Should().Equal(
                "Automation.Plan.Scout.Enabled",
                "Automation.Plan.Explore.Enabled",
                "Automation.Plan.Roles.Enabled",
                "Automation.Plan.Contract.Enabled",
                "Automation.Plan.ProbeDeployment.Enabled",
                "Automation.Plan.Survey.Enabled",
                "Automation.Plan.Mining.Enabled",
                "Automation.Plan.Siphon.Enabled",
                "Automation.Plan.Trading.Enabled",
                "Automation.Plan.SpareTime.Enabled");
    }

    [Fact]
    public void PlanFor_GivesThePlanThatAssignsEachKindOfGoal()
    {
        AutomationSwitches.PlanFor(new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-1" }).Should().Be(AutomationPlan.Scout);
        AutomationSwitches.PlanFor(new DeployProbeGoal { TargetWaypointSymbol = "X1-AB-1" }).Should().Be(AutomationPlan.ProbeDeployment);
        AutomationSwitches.PlanFor(new MineAndSellGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-AB-1", SellWaypointSymbol = "X1-AB-2" }).Should().Be(AutomationPlan.Mining);
        AutomationSwitches.PlanFor(new SiphonAndSellGoal { TradeSymbol = "HYDROCARBON", SourceWaypointSymbol = "X1-AB-1", SellWaypointSymbol = "X1-AB-2" }).Should().Be(AutomationPlan.Siphon);
        AutomationSwitches.PlanFor(new SurveyWaypointGoal { TargetWaypointSymbol = "X1-AB-1", TargetDepositSymbol = "IRON_ORE" }).Should().Be(AutomationPlan.Survey);
        AutomationSwitches.PlanFor(new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = "X1-AB-1", SellWaypointSymbol = "X1-AB-2" }).Should().Be(AutomationPlan.Trading);
        AutomationSwitches.PlanFor(new GatherAndSellGoal { SourceWaypointSymbol = "X1-AB-1" }).Should().Be(AutomationPlan.SpareTime);
        AutomationSwitches.PlanFor(new JumpGoal { GateWaypointSymbol = "X1-AB-I1", DestinationGateWaypointSymbol = "X1-CD-I2" }).Should().Be(AutomationPlan.Explore);
        AutomationSwitches.PlanFor(new ExploreSystemGoal { SystemSymbol = "X1-CD", Stops = ["X1-CD-I2"] }).Should().Be(AutomationPlan.Explore);
        AutomationSwitches.PlanFor(new IdleGoal()).Should().BeNull();
    }
}
