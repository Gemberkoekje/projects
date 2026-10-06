using FluentAssertions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Roles;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>
/// PLAN.md slice 6.29 (D96): "The role board values a ship abroad for trading only": mining, siphoning, surveys, contracts and
/// construction stay home (D60, D68), so a ship outside the systems the plans do business in, or on a trade trip that ends
/// outside them, can take the trade role and no other.
/// </summary>
public sealed class AbroadRoleTests
{
    [Fact]
    public void AShipAbroad_OrOnATripThatEndsAbroad_CanOnlyTrade()
    {
        var settings = Settings() with { BusinessSystems = new HashSet<string> { SystemSymbol } };

        settings.Available(CommandShip(), contractWantsOre: true).Should().Contain(FleetRole.Survey).And.Contain(FleetRole.Trade);
        settings.Available(CommandShip() with { SystemSymbol = "X1-CD" }, contractWantsOre: true).Should().Equal(FleetRole.Trade);
        settings.Available(CommandShip(), contractWantsOre: true, abroad: true).Should().Equal(FleetRole.Trade);
        Settings().Available(CommandShip() with { SystemSymbol = "X1-CD" }, contractWantsOre: true)
            .Should().Contain(FleetRole.Survey, "while the agent isn't known, no system counts as abroad");
        var tradingOff = Settings(AutomationPlan.Survey) with { BusinessSystems = new HashSet<string> { SystemSymbol } };
        tradingOff.Available(CommandShip() with { SystemSymbol = "X1-CD" }, contractWantsOre: false)
            .Should().BeEmpty("with the trading plan off, a ship abroad has no role");
    }

    private static RoleSettings Settings(params AutomationPlan[] on)
        => new(
            on.Length == 0 ? new HashSet<AutomationPlan> { AutomationPlan.Survey, AutomationPlan.Mining, AutomationPlan.Siphon, AutomationPlan.Trading } : new HashSet<AutomationPlan>(on),
            TimeSpan.FromMinutes(10),
            0.2,
            0.5,
            200,
            5_000);
}
