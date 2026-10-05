using FluentAssertions;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Roles;
using static SpaceTraders.Application.Tests.Construction.ConstructionFixture;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>
/// Slice 6.18, D83: the shuttle the mining plan bought for a far asteroid collects there, once a drone is parked there (D86),
/// kept in the collecting role by the role board. It does nothing else: it neither builds nor trades, and as it buys nothing,
/// its hold adds nothing to the credit reserve (D51).
/// </summary>
public sealed class CollectionRoleTests
{
    private static readonly FleetRole[] CargoRoles = [FleetRole.Trade, FleetRole.Construct];

    [Fact]
    public void AShuttleTheMiningPlanDesignated_Collects_BeforeTheLargestHoldsBuild()
    {
        // Two builders wanted: without its designation the shuttle would build too.
        var decisions = RolePlanner.Decide(
            [
                new RoleCandidate(Hauler(), CargoRoles, FleetRole.None, [new RoleOption(FleetRole.Trade, "trade|B", "trade|B", 30_000, 3_600)]),
                new RoleCandidate(Shuttle(), CargoRoles, FleetRole.Trade, [new RoleOption(FleetRole.Trade, "trade|C", "trade|C", 10_000, 3_600)]),
            ],
            contractWantsOre: false,
            headStart: 0.2,
            coverage: [],
            collectors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SHIP-7" },
            builders: 2);

        decisions.Single(decision => decision.ShipSymbol == "SHIP-7").Should().Match<RoleDecision>(decision => decision.Role == FleetRole.Collect && decision.Reason == RolePlanner.Collection);
        decisions.Single(decision => decision.ShipSymbol == "SHIP-6").Role.Should().Be(FleetRole.Construct);
    }

    [Fact]
    public void ACollector_CollectsForTheMiningPlan_AndIsNoTrader()
    {
        var board = FleetRoleBoard.For(rolesOn: true, surveyOn: true, spareTimeOn: false, new Dictionary<string, FleetRole> { ["SHIP-7"] = FleetRole.Collect });

        board.IsCollector(Shuttle()).Should().BeTrue();
        board.IsTrader(Shuttle()).Should().BeFalse();
        board.IsCollector(Hauler()).Should().BeFalse();
        FleetRoleBoard.For(rolesOn: false, surveyOn: true, spareTimeOn: false).IsCollector(Shuttle()).Should().BeFalse("the board designates no collectors when it is off");
    }

    [Fact]
    public void ACollectorsHold_AddsNothingToTheCreditReserve()
    {
        CreditReserve.Trades(Shuttle(), FleetRole.Collect).Should().BeFalse();
        CreditReserve.Trades(Shuttle(), FleetRole.Trade).Should().BeTrue();
        CreditReserve.TradingCargo([Shuttle(), Hauler()], ship => ship.Symbol == "SHIP-7" ? FleetRole.Collect : FleetRole.Trade).Should().Be(80);
    }
}
