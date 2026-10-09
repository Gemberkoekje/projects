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

    [Fact]
    public void AMiningDroneAbroad_AlsoMines_WithTheMiningPlan()
    {
        // Slice 6.40 (D122), asked on 2026-10-09: "I'd like mining to be done wherever there's low ore supply, not just in the home
        // area. However, outside of the home area, mining is low priority, and should never be in the way of trading." A drone
        // or an ore hound abroad mines in its system; the command ship, which could trade, still only trades there.
        var settings = Settings() with { BusinessSystems = new HashSet<string> { SystemSymbol } };
        var droneAbroad = Drone() with { SystemSymbol = "X1-CD", WaypointSymbol = "X1-CD-A1" };
        var houndAbroad = droneAbroad with { ShipType = "SHIP_ORE_HOUND", MountSymbols = ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_I"] };

        settings.Available(droneAbroad, contractWantsOre: false).Should().Equal(FleetRole.Mine, FleetRole.Trade);
        settings.Available(houndAbroad, contractWantsOre: false).Should().Equal(FleetRole.Mine, FleetRole.Trade);
        settings.Available(CommandShip() with { SystemSymbol = "X1-CD" }, contractWantsOre: true).Should().Equal(FleetRole.Trade);
        var miningOff = Settings(AutomationPlan.Trading) with { BusinessSystems = new HashSet<string> { SystemSymbol } };
        miningOff.Available(droneAbroad, contractWantsOre: true).Should().Equal([FleetRole.Trade], "the contract works at home");
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
