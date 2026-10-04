using FluentAssertions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using static SpaceTraders.Application.Tests.Construction.ConstructionFixture;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>
/// Slice 6.6 (D65): while the home system's jump gate needs materials, the ship with the largest hold that isn't a drone or
/// the surveyor builds it. Asked on 2026-10-04: "Can you implement a special role that works on this jump gate?" Supplying
/// pays nothing, so no estimate chooses the role: it goes first to the largest hold, as surveys go first (D38).
/// </summary>
public sealed class RolePlannerConstructionTests
{
    private static readonly FleetRole[] CommandRoles = [FleetRole.Survey, FleetRole.Mine, FleetRole.Siphon, FleetRole.Trade, FleetRole.Construct];
    private static readonly FleetRole[] CargoRoles = [FleetRole.Trade, FleetRole.Construct];
    private static readonly FleetRole[] DroneRoles = [FleetRole.Mine, FleetRole.Trade];

    [Fact]
    public void TheLargestHold_BuildsTheGate_AndTheOthersShareTheWork()
    {
        var decisions = RolePlanner.Decide(
            [
                Candidate(SurveyShip(), [FleetRole.Survey]),
                Candidate(CommandShip(), CommandRoles, Trade("trade|A", 20_000)),
                Candidate(Hauler(), CargoRoles, Trade("trade|B", 30_000)),
                Candidate(Shuttle(), CargoRoles, Trade("trade|C", 10_000)),
                Candidate(Drone(), DroneRoles, Trade("trade|D", 9_000)),
            ],
            contractWantsOre: false,
            headStart: 0.2,
            coverage: [],
            builders: 1);

        Role(decisions, "SHIP-6").Should().Be((FleetRole.Construct, RolePlanner.Construction));
        Role(decisions, "SHIP-1").Should().Be((FleetRole.Trade, RolePlanner.MostProfitable));
        Role(decisions, "SHIP-7").Should().Be((FleetRole.Trade, RolePlanner.MostProfitable));
        Role(decisions, "SHIP-3").Should().Be((FleetRole.Mine, RolePlanner.GathersFirst));
        Role(decisions, "SHIP-5").Should().Be((FleetRole.Survey, RolePlanner.OnlyRole));
    }

    [Fact]
    public void OfTwoEqualHolds_TheOneThatBuildsNow_KeepsIt()
    {
        var decisions = RolePlanner.Decide(
            [Candidate(Hauler("SHIP-6"), CargoRoles, Trade("trade|B", 30_000)), Candidate(Hauler("SHIP-8"), CargoRoles, FleetRole.Construct)],
            contractWantsOre: false,
            headStart: 0.2,
            coverage: [],
            builders: 1);

        Role(decisions, "SHIP-8").Should().Be((FleetRole.Construct, RolePlanner.Construction));
        Role(decisions, "SHIP-6").Should().Be((FleetRole.Trade, RolePlanner.MostProfitable));
    }

    [Fact]
    public void OfTwoEqualHolds_TheOneThatCanDoLeastElse_Builds()
    {
        // The shuttle, not the command ship, which can also survey, mine and siphon. A drone survey ship takes surveys.
        var decisions = RolePlanner.Decide(
            [Candidate(SurveyShip(), [FleetRole.Survey]), Candidate(CommandShip(), CommandRoles, Trade("trade|A", 20_000)), Candidate(Shuttle(), CargoRoles)],
            contractWantsOre: false,
            headStart: 0.2,
            coverage: [],
            builders: 1);

        Role(decisions, "SHIP-7").Should().Be((FleetRole.Construct, RolePlanner.Construction));
    }

    [Fact]
    public void MoreShipsBuild_WhenTheSettingSaysSo()
    {
        var decisions = RolePlanner.Decide(
            [Candidate(Hauler(), CargoRoles), Candidate(Shuttle(), CargoRoles), Candidate(Shuttle("SHIP-9"), CargoRoles)],
            contractWantsOre: false,
            headStart: 0.2,
            coverage: [],
            builders: 2);

        decisions.Where(decision => decision.Role == FleetRole.Construct).Select(decision => decision.ShipSymbol).Should().Equal("SHIP-6", "SHIP-7");
    }

    [Fact]
    public void TheShipThatSurveys_AndTheContractsMiners_DontBuild()
    {
        // The command ship is the only ship that can survey, and a drone can mine: it surveys (D38). The contract takes the
        // drone (D40). Nobody is left to build.
        var decisions = RolePlanner.Decide(
            [Candidate(CommandShip(), CommandRoles, Trade("trade|A", 20_000)), Candidate(Drone(), DroneRoles)],
            contractWantsOre: true,
            headStart: 0.2,
            coverage: [],
            builders: 1);

        decisions.Should().NotContain(decision => decision.Role == FleetRole.Construct);
        Role(decisions, "SHIP-1").Should().Be((FleetRole.Survey, RolePlanner.SurveyFirst));
    }

    [Fact]
    public void WhereTheGateNeedsNothing_NobodyBuilds()
    {
        // Without a site that needs materials the role isn't available (RoleSettings.Available): a hauler only trades.
        var decisions = RolePlanner.Decide([Candidate(Hauler(), [FleetRole.Trade], Trade("trade|B", 30_000))], contractWantsOre: false, headStart: 0.2, coverage: [], builders: 1);

        Role(decisions, "SHIP-6").Should().Be((FleetRole.Trade, RolePlanner.OnlyRole));
    }

    [Fact]
    public void AShipWhoseOneRoleIsBuilding_ButIsntChosen_HasNone()
    {
        // With the trading plan off, both shuttles could only build; one builds (D65), the other has no role.
        var decisions = RolePlanner.Decide(
            [Candidate(Shuttle(), [FleetRole.Construct]), Candidate(Shuttle("SHIP-9"), [FleetRole.Construct])],
            contractWantsOre: false,
            headStart: 0.2,
            coverage: [],
            builders: 1);

        Role(decisions, "SHIP-7").Should().Be((FleetRole.Construct, RolePlanner.Construction));
        Role(decisions, "SHIP-9").Should().Be((FleetRole.None, RolePlanner.NoWork));
    }

    [Fact]
    public void ABuilderThatALargerHoldReplaces_DoesntKeepTheRole()
    {
        // A hauler was bought: it builds, and the shuttle that built before goes back to trading, even without a route now.
        var decisions = RolePlanner.Decide(
            [Candidate(Shuttle(), CargoRoles, FleetRole.Construct), Candidate(Hauler(), CargoRoles)],
            contractWantsOre: false,
            headStart: 0.2,
            coverage: [],
            builders: 1);

        Role(decisions, "SHIP-6").Should().Be((FleetRole.Construct, RolePlanner.Construction));
        Role(decisions, "SHIP-7").Should().Be((FleetRole.Trade, RolePlanner.NoWork));
    }

    [Fact]
    public void EveryShipThatCanCarry_CanBuild_ButNoDrone()
    {
        FleetRoles.PotentialRoles(Hauler()).Should().Equal(FleetRole.Trade, FleetRole.Construct);
        FleetRoles.PotentialRoles(CommandShip()).Should().Equal(FleetRole.Survey, FleetRole.Mine, FleetRole.Siphon, FleetRole.Trade, FleetRole.Construct);
        FleetRoles.PotentialRoles(Drone()).Should().Equal(FleetRole.Mine, FleetRole.Trade);
        FleetRoles.CanConstruct(new ShipModel("SHIP-4", SystemSymbol, H51, "DOCKED", "CRUISE", 80, 80, CargoCapacity: 15, ShipType: "SHIP_SIPHON_DRONE")).Should().BeFalse();
        FleetRoles.CanConstruct(SurveyShip()).Should().BeFalse();
    }

    private static (FleetRole Role, string Reason) Role(IReadOnlyList<RoleDecision> decisions, string ship)
        => decisions.Single(decision => decision.ShipSymbol == ship) is var decision ? (decision.Role, decision.Reason) : default;

    private static RoleCandidate Candidate(ShipModel ship, IReadOnlyList<FleetRole> roles, params RoleOption[] options)
        => new(ship, roles, FleetRole.None, options);

    private static RoleCandidate Candidate(ShipModel ship, IReadOnlyList<FleetRole> roles, FleetRole current, params RoleOption[] options)
        => new(ship, roles, current, options);

    private static RoleOption Trade(string key, int perHour) => new(FleetRole.Trade, key, key, perHour, 3_600);

    /// <summary>A survey ship, as bought (D47): a surveyor, an 80-unit tank, and nothing to carry anything in.</summary>
    private static ShipModel SurveyShip()
        => new("SHIP-5", SystemSymbol, H51, "IN_ORBIT", "CRUISE", 80, 80, ShipType: "SHIP_SURVEYOR", MountSymbols: ["MOUNT_SURVEYOR_I"]);
}
