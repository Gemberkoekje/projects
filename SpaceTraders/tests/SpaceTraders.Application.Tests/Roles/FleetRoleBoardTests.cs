using FluentAssertions;
using SpaceTraders.Application.Roles;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>
/// Slice 6.9: every plan reads which ships it may give work from the board. Off, the fixed rules hold as before (D20,
/// D34); on, the roles the board gave.
/// </summary>
public sealed class FleetRoleBoardTests
{
    [Fact]
    public void Off_TheFixedRulesHold()
    {
        var board = FleetRoleBoard.For(rolesOn: false, surveyOn: true, spareTimeOn: true);

        // D20: with the survey plan on, the command ship surveys, and only that; in its spare time it gathers (D34).
        board.IsSurveyor(CommandShip()).Should().BeTrue();
        board.IsMiner(CommandShip()).Should().BeFalse();
        board.MinesForContract(CommandShip()).Should().BeFalse();
        board.IsTrader(CommandShip()).Should().BeFalse();
        board.GathersInSpareTime(CommandShip()).Should().BeTrue();

        board.IsMiner(Drone()).Should().BeTrue();
        board.MinesForContract(Drone()).Should().BeTrue();
        board.IsTrader(Drone()).Should().BeTrue();
    }

    [Fact]
    public void On_TheShipsWorkByTheRolesTheBoardGave()
    {
        var board = FleetRoleBoard.For(
            rolesOn: true,
            surveyOn: true,
            spareTimeOn: true,
            new Dictionary<string, FleetRole> { ["SHIP-1"] = FleetRole.Trade, ["SHIP-3"] = FleetRole.Mine, ["SHIP-4"] = FleetRole.Trade });

        // The command ship, with the trade role, trades: it doesn't survey, and the contract may take it (D40).
        board.IsTrader(CommandShip()).Should().BeTrue();
        board.IsSurveyor(CommandShip()).Should().BeFalse();
        board.IsMiner(CommandShip()).Should().BeFalse();
        board.MinesForContract(CommandShip()).Should().BeTrue();
        board.GathersInSpareTime(CommandShip()).Should().BeFalse();

        // A drone with the mining role mines, and trades when the mining plan has no trip for it; one with the trade role
        // only trades, but the contract still takes it.
        board.IsMiner(Drone("SHIP-3")).Should().BeTrue();
        board.IsTrader(Drone("SHIP-3")).Should().BeTrue();
        board.IsMiner(Drone("SHIP-4")).Should().BeFalse();
        board.MinesForContract(Drone("SHIP-4")).Should().BeTrue();
    }

    [Fact]
    public void On_TheShipWithTheSurveyRole_Surveys_GathersInItsSpareTime_AndIsLeftToItByTheContract()
    {
        var board = FleetRoleBoard.For(rolesOn: true, surveyOn: true, spareTimeOn: true, new Dictionary<string, FleetRole> { ["SHIP-1"] = FleetRole.Survey });

        board.IsSurveyor(CommandShip()).Should().BeTrue();
        board.GathersInSpareTime(CommandShip()).Should().BeTrue();
        board.MinesForContract(CommandShip()).Should().BeFalse();
        board.IsTrader(CommandShip()).Should().BeFalse();
    }

    [Fact]
    public void On_AShipTheBoardHasntSeen_WaitsForItsRole_ButTheContractMayTakeIt()
    {
        // A drone bought this tick: the next tick's evaluation gives it a role. The contract plan buys its own drone.
        var board = FleetRoleBoard.For(rolesOn: true, surveyOn: true, spareTimeOn: true);

        board.IsMiner(Drone()).Should().BeFalse();
        board.IsTrader(Drone()).Should().BeFalse();
        board.IsSiphoner(Drone()).Should().BeFalse();
        board.MinesForContract(Drone()).Should().BeTrue();
    }
}
