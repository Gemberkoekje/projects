using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Tests.Roles;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Orchestration;

public sealed class BudgetPolicyTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly ISettingsRepository _settings = ReserveSettings();
    private readonly FullHoldSavings _savings = new();

    public BudgetPolicyTests() => Fleet();

    private static IAgentRepository MakeAgent(long credits)
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(
            new AgentModel("AGENT-1", null, null, credits, "COSMIC", 1));
        return agents;
    }

    private static ISettingsRepository MakeSettings(long reserve, int fabMatsThreshold = 2_500, int fabMatsTransactionSize = 60, bool hourlyCapEnabled = false)
    {
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<long>("FleetExpansion.MinCreditReserve", Arg.Any<CancellationToken>()).Returns(reserve);
        settings.GetAsync<int>("Construction.FabMatsBuyThreshold", Arg.Any<CancellationToken>()).Returns(fabMatsThreshold);
        settings.GetAsync<int>("Construction.FabMatsTransactionSize", Arg.Any<CancellationToken>()).Returns(fabMatsTransactionSize);
        settings.GetAsync<bool>("Construction.HourlyBudgetCapEnabled", Arg.Any<CancellationToken>()).Returns(hourlyCapEnabled);
        return settings;
    }

    [Fact]
    public async Task EvaluateAsync_AllowsSpendBelowSpendable()
    {
        var policy = Policy(MakeAgent(500_000), MakeSettings(100_000));
        var decision = await policy.EvaluateAsync(50_000, CancellationToken.None);

        decision.CanAfford.Should().BeTrue();
        decision.SpendableCredits.Should().Be(400_000);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsSpendThatBreachesReserve()
    {
        var policy = Policy(MakeAgent(150_000), MakeSettings(100_000));
        var decision = await policy.EvaluateAsync(80_000, CancellationToken.None);

        decision.CanAfford.Should().BeFalse();
        decision.SpendableCredits.Should().Be(50_000);
        decision.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateAsync_ZeroProposedCost_AlwaysAffordable()
    {
        var policy = Policy(MakeAgent(1_000), MakeSettings(100_000));
        var decision = await policy.EvaluateAsync(0, CancellationToken.None);

        decision.CanAfford.Should().BeTrue();
        decision.SpendableCredits.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsFabMatsSettings()
    {
        var policy = Policy(MakeAgent(500_000), MakeSettings(100_000, fabMatsThreshold: 2_400, fabMatsTransactionSize: 55, hourlyCapEnabled: false));
        var decision = await policy.EvaluateAsync(0, CancellationToken.None);

        decision.FabMatsBuyThreshold.Should().Be(2_400);
        decision.FabMatsTransactionSize.Should().Be(55);
        decision.HourlyConstructionBudgetCapEnabled.Should().BeFalse();
    }

    /// <summary>
    /// D51 (asked on 2026-10-03): "What if we made the amount of credits for trade wider based on the amount of cargo total
    /// in the fleet? Something like: 60.000 hard minimum, 1.000 per cargo hold." The reserve a purchase keeps is the floor
    /// and 1,000 a unit of what the ships that trade can carry: the cargo ships and the command ship. The drones that gather
    /// and the probes buy no cargo.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 100_000)]
    [InlineData(1, 0, 140_000)]
    [InlineData(1, 1, 220_000)]
    [InlineData(1, 2, 300_000)]
    public async Task TheReserve_GrowsWithWhatTheTradingShipsCanCarry(int shuttles, int haulers, long reserve)
    {
        // Today the command ship's 40 units; a light shuttle carries 40, a light hauler 80. With the shuttle, the first light
        // hauler needs 354,210 credits and 140,000 left over.
        Fleet(
        [
            CommandShip(),
            StartingProbe(),
            Drone("SHIP-3"),
            .. Enumerable.Range(0, shuttles).Select(index => CargoShip($"SHIP-S{index}", "SHIP_LIGHT_SHUTTLE", 40)),
            .. Enumerable.Range(0, haulers).Select(index => CargoShip($"SHIP-H{index}", "SHIP_LIGHT_HAULER", 80)),
        ]);

        var decision = await Policy(MakeAgent(1_000_000), _settings).EvaluateAsync(354_210, CancellationToken.None);

        decision.ReservedCredits.Should().Be(reserve);
        decision.SpendableCredits.Should().Be(1_000_000 - reserve);
    }

    [Fact]
    public async Task ADroneTheRoleBoardHasTrading_RaisesTheReserve_WhileItHoldsThatRole()
    {
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Trade), ("SHIP-3", FleetRole.Trade), ("SHIP-4", FleetRole.Mine));
        Fleet(CommandShip(), Drone("SHIP-3"), Drone("SHIP-4"));

        var decision = await Policy(MakeAgent(1_000_000), _settings).EvaluateAsync(50_000, CancellationToken.None);

        decision.ReservedCredits.Should().Be(115_000);
    }

    [Fact]
    public async Task WhileATraderSavesUpForAFullHold_TheReserveGrowsByTheDearest_UntilItIsBought()
    {
        // D56: "the credit floor should be temporarily expanded so any ship purchases wait for the full hold to be bought".
        Fleet(StartingProbe(), Drone("SHIP-3"), Drone("SHIP-4"));
        _savings.SaveFor("SHIP-1", "X1-AB-K85|X1-AB-D41|EQUIPMENT", 130_312);
        _savings.SaveFor("SHIP-5", "X1-AB-D41|X1-AB-A1|MEDICINE", 194_922);

        var decision = await Policy(MakeAgent(1_000_000), _settings).EvaluateAsync(50_000, CancellationToken.None);

        decision.ReservedCredits.Should().Be(60_000 + 194_922);

        _savings.Clear("SHIP-5");
        _savings.Clear("SHIP-1");

        (await Policy(MakeAgent(1_000_000), _settings).EvaluateAsync(50_000, CancellationToken.None)).ReservedCredits.Should().Be(60_000);
    }

    [Fact]
    public async Task WithoutAShipThatTrades_TheReserveIsTheFloor()
    {
        Fleet(StartingProbe(), Drone("SHIP-3"), Drone("SHIP-4"));

        var decision = await Policy(MakeAgent(1_000_000), _settings).EvaluateAsync(50_000, CancellationToken.None);

        decision.ReservedCredits.Should().Be(60_000);
    }

    [Theory]
    [InlineData(null, 100_000)]
    [InlineData("0", 60_000)]
    [InlineData("500", 80_000)]
    public async Task TheCreditsPerUnit_AreASetting_AThousandWithoutIt(string? perUnit, long reserve)
    {
        Fleet(CommandShip());
        _settings.GetRawAsync("FleetExpansion.ReservePerTradingCargoUnit", Arg.Any<CancellationToken>()).Returns(perUnit);

        var decision = await Policy(MakeAgent(1_000_000), _settings).EvaluateAsync(50_000, CancellationToken.None);

        decision.ReservedCredits.Should().Be(reserve);
    }

    /// <summary>The seeded settings of D51: a floor of 60,000, and 1,000 a unit of trading hold.</summary>
    private static ISettingsRepository ReserveSettings()
    {
        var settings = MakeSettings(60_000);
        settings.GetRawAsync("FleetExpansion.ReservePerTradingCargoUnit", Arg.Any<CancellationToken>()).Returns("1000");
        return settings;
    }

    private static ShipModel StartingProbe()
        => new("SHIP-2", SystemSymbol, H52, "DOCKED", "CRUISE", 0, 0, ShipType: "SATELLITE");

    private static ShipModel CargoShip(string symbol, string shipType, int cargo)
        => new(symbol, SystemSymbol, H52, "DOCKED", "CRUISE", 600, 600, CargoCapacity: cargo, ShipType: shipType, MountSymbols: ["MOUNT_TURRET_I"]);

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private BudgetPolicy Policy(IAgentRepository agents, ISettingsRepository settings) => new(agents, settings, _ships, _plans, _savings);
}
