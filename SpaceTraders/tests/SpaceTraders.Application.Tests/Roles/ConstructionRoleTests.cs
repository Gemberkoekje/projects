using FluentAssertions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Roles;
using static SpaceTraders.Application.Tests.Construction.ConstructionFixture;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>
/// Slice 6.6: the construction role counts while the construction plan is on and the jump gate of the ship's system needs
/// materials (D63: the home system's only); a builder trades when the construction plan has nothing it may buy (D60).
/// </summary>
public sealed class ConstructionRoleTests
{
    [Fact]
    public void TheConstructionRole_CountsWithThePlanOn_WhereTheGateNeedsMaterials()
    {
        var settings = Settings(AutomationPlan.Trading, AutomationPlan.Construction);
        var gateNeedsMaterials = settings with { ConstructionSystems = new HashSet<string> { SystemSymbol } };
        var gateElsewhere = settings with { ConstructionSystems = new HashSet<string> { "X1-HZ59" } };
        var planOff = Settings(AutomationPlan.Trading) with { ConstructionSystems = new HashSet<string> { SystemSymbol } };

        settings.Available(Hauler(), contractWantsOre: false).Should().Equal(FleetRole.Trade);
        gateNeedsMaterials.Available(Hauler(), contractWantsOre: false).Should().Equal(FleetRole.Trade, FleetRole.Construct);
        gateElsewhere.Available(Hauler(), contractWantsOre: false).Should().Equal(FleetRole.Trade);
        planOff.Available(Hauler(), contractWantsOre: false).Should().Equal(FleetRole.Trade);
    }

    [Fact]
    public void ABuilder_BuildsForTheConstructionPlan_AndTradesForTheTradingPlan()
    {
        var board = FleetRoleBoard.For(rolesOn: true, surveyOn: true, spareTimeOn: false, new Dictionary<string, FleetRole> { ["SHIP-6"] = FleetRole.Construct });

        board.IsBuilder(Hauler()).Should().BeTrue();
        board.IsTrader(Hauler()).Should().BeTrue();
        board.IsBuilder(Shuttle()).Should().BeFalse();
        FleetRoleBoard.For(rolesOn: false, surveyOn: true, spareTimeOn: false).IsBuilder(Hauler()).Should().BeFalse();
    }

    private static RoleSettings Settings(params AutomationPlan[] on)
        => new(new HashSet<AutomationPlan>(on), TimeSpan.FromMinutes(10), 0.2, 0.5, 200, 5_000);
}
