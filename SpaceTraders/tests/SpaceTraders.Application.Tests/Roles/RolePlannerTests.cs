using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>
/// Slice 6.9 (D38): every ship takes the role that earns the fleet the most per hour, by what it and the others can
/// do. Asked on 2026-10-02: "If there is only 1 ship that can survey, then that ship should prioritize surveying. But
/// if there are 2 ships that can survey, but one of them can only survey and the other can survey, mine, trade and
/// siphon, the ship that can only survey should take the job."
/// </summary>
public sealed class RolePlannerTests
{
    private static readonly FleetRole[] CommandRoles = [FleetRole.Survey, FleetRole.Mine, FleetRole.Siphon, FleetRole.Trade];
    private static readonly FleetRole[] DroneRoles = [FleetRole.Mine, FleetRole.Trade];
    private static readonly FleetRole[] SiphonRoles = [FleetRole.Siphon, FleetRole.Trade];

    [Fact]
    public void TheOnlyShipThatCanSurvey_Surveys_ThoughTradingPaysItMore()
    {
        var decisions = RolePlanner.Decide(
            [
                Candidate(CommandShip(), CommandRoles, Trade("trade|A", 20_000), Mine("mine|H51|COPPER_ORE", 5_000)),
                Candidate(Drone(), DroneRoles, Mine("mine|F49|SILICON_CRYSTALS", 3_000)),
            ],
            contractWantsOre: false,
            headStart: 0.2);

        Role(decisions, "SHIP-1").Should().Be((FleetRole.Survey, RolePlanner.SurveyFirst));
        Role(decisions, "SHIP-3").Should().Be((FleetRole.Mine, RolePlanner.MostProfitable));
    }

    [Fact]
    public void AShipThatCanOnlySurvey_Surveys_AndTheCommandShipTakesWhatPaysItMost()
    {
        var decisions = RolePlanner.Decide(
            [
                Candidate(Surveyor(), [FleetRole.Survey]),
                Candidate(CommandShip(), CommandRoles, Trade("trade|A", 20_000), Mine("mine|H51|COPPER_ORE", 5_000)),
                Candidate(Drone(), DroneRoles, Mine("mine|F49|SILICON_CRYSTALS", 3_000)),
            ],
            contractWantsOre: false,
            headStart: 0.2);

        Role(decisions, "SHIP-5").Should().Be((FleetRole.Survey, RolePlanner.OnlyRole));
        Role(decisions, "SHIP-1").Should().Be((FleetRole.Trade, RolePlanner.MostProfitable));
        decisions.Single(decision => decision.ShipSymbol == "SHIP-1").Option!.JobKey.Should().Be("trade|A");
        Role(decisions, "SHIP-3").Should().Be((FleetRole.Mine, RolePlanner.MostProfitable));
    }

    [Fact]
    public void OfTwoShipsThatCanSurveyAndMore_TheOneWithLessToLose_Surveys()
    {
        var decisions = RolePlanner.Decide(
            [
                Candidate(CommandShip(symbol: "SHIP-1"), CommandRoles, Trade("trade|A", 20_000)),
                Candidate(CommandShip(symbol: "SHIP-2"), CommandRoles, Trade("trade|B", 8_000)),
                Candidate(Drone(), DroneRoles, Mine("mine|F49|SILICON_CRYSTALS", 3_000)),
            ],
            contractWantsOre: false,
            headStart: 0.2);

        Role(decisions, "SHIP-2").Should().Be((FleetRole.Survey, RolePlanner.SurveyFirst));
        Role(decisions, "SHIP-1").Should().Be((FleetRole.Trade, RolePlanner.MostProfitable));
    }

    [Theory]
    [InlineData(9_000, "SHIP-1")]
    [InlineData(10_000, "SHIP-2")]
    public void TheShipSurveyingNow_KeepsIt_UnlessAnotherWouldLoseLessByMoreThanTheHeadStart(int holderBest, string surveyor)
    {
        // SHIP-1 surveys now; SHIP-2 would give up 8,000 an hour. With a 20% head start SHIP-1 keeps it while it gives up
        // at most 9,600.
        var decisions = RolePlanner.Decide(
            [
                Candidate(CommandShip(symbol: "SHIP-1"), CommandRoles, FleetRole.Survey, Trade("trade|A", holderBest)),
                Candidate(CommandShip(symbol: "SHIP-2"), CommandRoles, Trade("trade|B", 8_000)),
                Candidate(Drone(), DroneRoles, Mine("mine|F49|SILICON_CRYSTALS", 3_000)),
            ],
            contractWantsOre: false,
            headStart: 0.2);

        decisions.Where(decision => decision.Role == FleetRole.Survey).Select(decision => decision.ShipSymbol).Should().Equal(surveyor);
    }

    [Fact]
    public void WithNoOtherShipThatCanMine_NobodySurveys()
    {
        // Surveys are for miners: the command ship alone takes what pays it most.
        var decisions = RolePlanner.Decide(
            [Candidate(CommandShip(), CommandRoles, Trade("trade|A", 20_000), Mine("mine|H51|COPPER_ORE", 5_000))],
            contractWantsOre: false,
            headStart: 0.2);

        Role(decisions, "SHIP-1").Should().Be((FleetRole.Trade, RolePlanner.MostProfitable));
    }

    [Fact]
    public void WhileTheContractWantsOre_EveryShipThatCanMineButTheSurveyorMines_ThoughTradingPaysMore()
    {
        // D40, D23 kept: the contract comes first, so it can't stall because a drone found trading more profitable.
        var decisions = RolePlanner.Decide(
            [
                Candidate(CommandShip(), CommandRoles, Trade("trade|A", 20_000)),
                Candidate(Drone(), DroneRoles, Trade("trade|B", 10_000), Mine("mine|F49|SILICON_CRYSTALS", 2_000)),
            ],
            contractWantsOre: true,
            headStart: 0.2);

        Role(decisions, "SHIP-1").Should().Be((FleetRole.Survey, RolePlanner.SurveyFirst));
        Role(decisions, "SHIP-3").Should().Be((FleetRole.Mine, RolePlanner.Contract));
    }

    [Fact]
    public void TheWorkGoesWhereItEarnsTheFleetMost_NotToTheFirstShipThatWantsIt()
    {
        // One route: the command ship earns 20,000 an hour on it and the drone 4,000. The drone would rather trade than mine
        // (3,000), but the fleet earns 23,000 with the command ship trading and the drone mining, against 9,000 the other
        // way round.
        var decisions = RolePlanner.Decide(
            [
                Candidate(Drone(), DroneRoles, Trade("trade|R", 4_000), Mine("mine|F49|SILICON_CRYSTALS", 3_000)),
                Candidate(CommandShip(), [FleetRole.Mine, FleetRole.Trade], Trade("trade|R", 20_000), Mine("mine|H51|COPPER_ORE", 5_000)),
            ],
            contractWantsOre: false,
            headStart: 0.2);

        Role(decisions, "SHIP-1").Should().Be((FleetRole.Trade, RolePlanner.MostProfitable));
        Role(decisions, "SHIP-3").Should().Be((FleetRole.Mine, RolePlanner.MostProfitable));
    }

    [Theory]
    [InlineData(3_500, FleetRole.Trade)]
    [InlineData(4_000, FleetRole.Mine)]
    public void AShipKeepsItsRole_UnlessAnotherPaysMoreThanTheHeadStart(int minePerHour, FleetRole expected)
    {
        // D41: trading pays 3,000 an hour, so with a 20% head start mining must beat 3,600.
        var decisions = RolePlanner.Decide(
            [Candidate(Drone(), DroneRoles, FleetRole.Trade, Trade("trade|R", 3_000), Mine("mine|F49|SILICON_CRYSTALS", minePerHour))],
            contractWantsOre: false,
            headStart: 0.2);

        decisions.Single().Role.Should().Be(expected);
    }

    [Fact]
    public void AShipWithoutATrip_KeepsItsRole_AndANewOneTakesItsFirstRoleButSurveying()
    {
        var decisions = RolePlanner.Decide(
            [
                Candidate(Drone("SHIP-3"), DroneRoles, FleetRole.Trade),
                Candidate(Drone("SHIP-4"), DroneRoles),
            ],
            contractWantsOre: false,
            headStart: 0.2);

        Role(decisions, "SHIP-3").Should().Be((FleetRole.Trade, RolePlanner.NoWork));
        Role(decisions, "SHIP-4").Should().Be((FleetRole.Mine, RolePlanner.NoWork));
    }

    [Fact]
    public void ADroneWorkingOnAScarceMineral_KeepsGatheringIt_ThoughTradingPaysItMore()
    {
        // Slice 6.10b (D48): "at least 1 drone per mineral that is scarce or limited". Without it SHIP-4 would trade, and the
        // mining plan would buy a drone for copper, and another.
        var decisions = RolePlanner.Decide(
            [
                Candidate(Drone("SHIP-3"), DroneRoles, FleetRole.Mine, Trade("trade|A", 10_000), Mine("mine|H51|IRON_ORE", 2_000)),
                Candidate(Drone("SHIP-4"), DroneRoles, FleetRole.Mine, Trade("trade|B", 9_000), Mine("mine|H51|COPPER_ORE", 2_000)),
            ],
            contractWantsOre: false,
            headStart: 0.2,
            [new MineralCoverage("COPPER_ORE", FleetRole.Mine, ["SHIP-3", "SHIP-4"], ["SHIP-4"])]);

        Role(decisions, "SHIP-4").Should().Be((FleetRole.Mine, RolePlanner.Coverage));
        Role(decisions, "SHIP-3").Should().Be((FleetRole.Trade, RolePlanner.MostProfitable));
    }

    [Fact]
    public void ForAScarceMineralNobodyWorksOn_TheDroneWithTheLeastToLose_IsKept()
    {
        var decisions = RolePlanner.Decide(
            [
                Candidate(Drone("SHIP-3"), DroneRoles, FleetRole.Trade, Trade("trade|A", 10_000), Mine("mine|F49|QUARTZ_SAND", 1_000)),
                Candidate(Drone("SHIP-4"), DroneRoles, FleetRole.Trade, Trade("trade|B", 4_000), Mine("mine|F49|QUARTZ_SAND", 1_000)),
            ],
            contractWantsOre: false,
            headStart: 0.2,
            [new MineralCoverage("QUARTZ_SAND", FleetRole.Mine, ["SHIP-3", "SHIP-4"], [])]);

        Role(decisions, "SHIP-4").Should().Be((FleetRole.Mine, RolePlanner.Coverage));
        Role(decisions, "SHIP-3").Should().Be((FleetRole.Trade, RolePlanner.MostProfitable));
    }

    [Fact]
    public void EachScarceMineral_KeepsOneDrone_AndTheRestShareTheWork()
    {
        // Two scarce gases and three siphon drones: the two with the least to lose keep siphoning, the third takes what pays
        // it most.
        var decisions = RolePlanner.Decide(
            [
                Candidate(Drone("SHIP-5"), SiphonRoles, Trade("trade|A", 6_000), Siphon("siphon|G50|LIQUID_HYDROGEN", 3_000)),
                Candidate(Drone("SHIP-6"), SiphonRoles, Trade("trade|B", 5_000), Siphon("siphon|E47|LIQUID_NITROGEN", 3_000)),
                Candidate(Drone("SHIP-7"), SiphonRoles, Trade("trade|C", 7_000), Siphon("siphon|G50|HYDROCARBON", 3_000)),
            ],
            contractWantsOre: false,
            headStart: 0.2,
            [
                new MineralCoverage("LIQUID_HYDROGEN", FleetRole.Siphon, ["SHIP-5", "SHIP-6", "SHIP-7"], []),
                new MineralCoverage("LIQUID_NITROGEN", FleetRole.Siphon, ["SHIP-5", "SHIP-6", "SHIP-7"], []),
            ]);

        decisions.Where(decision => decision.Reason == RolePlanner.Coverage).Select(decision => (decision.ShipSymbol, decision.Role))
            .Should().BeEquivalentTo([("SHIP-5", FleetRole.Siphon), ("SHIP-6", FleetRole.Siphon)]);
        Role(decisions, "SHIP-7").Should().Be((FleetRole.Trade, RolePlanner.MostProfitable));
    }

    [Fact]
    public void TheCommandShip_IsNoDroneToKeep_AndTheContractsMinersComeFirst()
    {
        // The command ship can survey; while the contract wants ore, the drone mines for it already (D40).
        var decisions = RolePlanner.Decide(
            [
                Candidate(CommandShip(), CommandRoles, Trade("trade|A", 20_000), Mine("mine|H51|COPPER_ORE", 5_000)),
                Candidate(Drone(), DroneRoles, Trade("trade|B", 10_000), Mine("mine|F49|SILICON_CRYSTALS", 2_000)),
            ],
            contractWantsOre: true,
            headStart: 0.2,
            [new MineralCoverage("COPPER_ORE", FleetRole.Mine, ["SHIP-1", "SHIP-3"], ["SHIP-1"])]);

        Role(decisions, "SHIP-1").Should().Be((FleetRole.Survey, RolePlanner.SurveyFirst));
        Role(decisions, "SHIP-3").Should().Be((FleetRole.Mine, RolePlanner.Contract));
    }

    [Fact]
    public void AShipWithNoRoleWhosePlanIsOn_HasNone()
    {
        var decisions = RolePlanner.Decide([Candidate(Drone(), [])], contractWantsOre: false, headStart: 0.2);

        decisions.Single().Should().Be(new RoleDecision("SHIP-3", FleetRole.None, RolePlanner.NoRole, null));
    }

    private static (FleetRole Role, string Reason) Role(IReadOnlyList<RoleDecision> decisions, string ship)
        => decisions.Single(decision => decision.ShipSymbol == ship) is var decision ? (decision.Role, decision.Reason) : default;

    private static RoleCandidate Candidate(ShipModel ship, IReadOnlyList<FleetRole> roles, params RoleOption[] options)
        => new(ship, roles, FleetRole.None, options);

    private static RoleCandidate Candidate(ShipModel ship, IReadOnlyList<FleetRole> roles, FleetRole current, params RoleOption[] options)
        => new(ship, roles, current, options);

    /// <summary>A trip worth <paramref name="perHour"/> credits an hour: that many credits in an hour.</summary>
    private static RoleOption Trade(string key, int perHour) => new(FleetRole.Trade, key, key, perHour, 3_600);

    private static RoleOption Mine(string key, int perHour) => new(FleetRole.Mine, key, key, perHour, 3_600);

    private static RoleOption Siphon(string key, int perHour) => new(FleetRole.Siphon, key, key, perHour, 3_600);

    /// <summary>A survey ship: a surveyor and nothing to carry anything in.</summary>
    private static ShipModel Surveyor()
        => new("SHIP-5", SystemSymbol, XB5C, "IN_ORBIT", "CRUISE", 80, 80, ShipType: "SHIP_SURVEYOR", MountSymbols: ["MOUNT_SURVEYOR_I"]);
}
