using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>
/// Slice 6.9: with the role board on, a drone the mining (or siphon) plan buys must take that role, or it would trade and
/// the plan would buy the next for the same opening.
/// </summary>
public sealed class RoleAdvisorTests
{
    private readonly IMiningContextReader _contexts = Substitute.For<IMiningContextReader>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();

    public RoleAdvisorTests()
    {
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _settings.GetAsync<int>("Trade.MinProfitPerUnit", Arg.Any<CancellationToken>()).Returns(200);
        On(AutomationPlan.Mining, AutomationPlan.Trading);
    }

    [Fact]
    public async Task ADroneWithNothingToTrade_WouldMine()
    {
        // The middle of X1-DC53 has no lucrative route; mining pays.
        (await Advisor().WouldTakeAsync(NewDrone(), FleetRole.Mine, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task ADroneThatWouldEarnMoreTrading_WouldNotMine()
    {
        // H52, where the drone is bought, sells MACHINERY for 100 that H51, 0.1 away, buys for 2,000.
        var markets = Markets()
            .Select(market => market.WaypointSymbol switch
            {
                H52 => market with { TradeGoods = [.. market.TradeGoods, Good("MACHINERY", "EXPORT", 100, 50, 60, "MODERATE")] },
                H51 => market with { TradeGoods = [.. market.TradeGoods, Good("MACHINERY", "IMPORT", 4_000, 2_000, 60, "MODERATE")] },
                _ => market,
            })
            .ToArray();
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(Map(markets), [], 129_357, Now));

        (await Advisor().WouldTakeAsync(NewDrone(), FleetRole.Mine, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task ARoleWhosePlanIsOff_IsNotTaken_AndTheOnlyRoleLeft_Is()
    {
        On(AutomationPlan.Trading);
        (await Advisor().WouldTakeAsync(NewDrone(), FleetRole.Mine, CancellationToken.None)).Should().BeFalse();

        On(AutomationPlan.Mining);
        (await Advisor().WouldTakeAsync(NewDrone(), FleetRole.Mine, CancellationToken.None)).Should().BeTrue();
    }

    /// <summary>A drone as the mining plan would buy it at H52, before startup sync records its mounts.</summary>
    private static ShipModel NewDrone()
        => new("NEW-DRONE", SystemSymbol, H52, "DOCKED", "CRUISE", 80, 80, CargoCapacity: 15, ShipType: "SHIP_MINING_DRONE");

    private void On(params AutomationPlan[] plans)
    {
        foreach (var plan in Enum.GetValues<AutomationPlan>())
        {
            _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(plan), Arg.Any<CancellationToken>()).Returns(plans.Contains(plan));
        }
    }

    private RoleAdvisor Advisor() => new(_contexts, _settings, new GatheringRates());
}
