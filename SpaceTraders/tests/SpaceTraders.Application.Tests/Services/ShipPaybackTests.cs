using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// Slice 6.70 (D150), asked on 2026-11-15: "Does a ship earn it's own price back before the end of the week?" In the 14 hours
/// before that day's reset the bot bought 26 bulk freighters for 105M; one earned about 200,000 an hour, 20 hours for its 3.9M.
/// </summary>
public sealed class ShipPaybackTests
{
    private readonly PaybackFixture _fixture = new();

    [Fact]
    public async Task ABulkFreighter_TwentyHoursFromPayingBack_IsNotBought_SeventeenHoursBeforeTheReset()
    {
        _fixture.ResetIn(hours: 17.7);
        _fixture.Have(Enumerable.Range(1, 10).Select(index => PaybackFixture.Owned($"FREIGHTER-{index}", "SHIP_BULK_FREIGHTER", net: 4_800_000)).ToArray());

        var verdict = await _fixture.Payback().JudgeAsync("SHIP_BULK_FREIGHTER", 3_900_000, PaybackFixture.Now, CancellationToken.None);

        verdict.PaysBack.Should().BeFalse();
        verdict.Basis.Should().Be(ShipPayback.FromRecord);
        verdict.CreditsPerHour.Should().Be(200_000);
        verdict.PaybackHours.Should().BeApproximately(20.5, 0.001);
        verdict.ToString().Should().Be("a SHIP_BULK_FREIGHTER for 3900000 earns 200000 an hour by its record: 20.5 hours with the first 1, 17.7 left before the reset at 2026-11-16 00:42:00Z (D150)");
    }

    [Fact]
    public async Task ABulkFreighter_IsBought_WhileItPaysBackBeforeTheReset()
    {
        _fixture.ResetIn(hours: 21);
        _fixture.Have(Enumerable.Range(1, 10).Select(index => PaybackFixture.Owned($"FREIGHTER-{index}", "SHIP_BULK_FREIGHTER", net: 4_800_000)).ToArray());

        (await _fixture.Payback().JudgeAsync("SHIP_BULK_FREIGHTER", 3_900_000, PaybackFixture.Now, CancellationToken.None)).PaysBack.Should().BeTrue();
    }

    [Theory]
    [InlineData(11, "", true)]
    [InlineData(10.9, "", false)]
    [InlineData(10, "0", true)]
    [InlineData(12, "3", false)]
    public async Task TheFirstHours_EarnNothing(double hoursLeft, string leadHours, bool paysBack)
    {
        // A ship bought spends its first hour reaching its work: 100,000 an hour pays 1M in 10 hours, 11 with that hour.
        _fixture.ResetIn(hoursLeft);
        _fixture.Settings.GetRawAsync(ShipPayback.LeadSetting, Arg.Any<CancellationToken>()).Returns(leadHours);
        _fixture.Have(PaybackFixture.Owned("HAULER-1", "SHIP_LIGHT_HAULER", net: 2_400_000));

        (await _fixture.Payback().JudgeAsync("SHIP_LIGHT_HAULER", 1_000_000, PaybackFixture.Now, CancellationToken.None)).PaysBack.Should().Be(paysBack);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a time")]
    [InlineData("2026-11-15T06:00:00.0000000+00:00")]
    public async Task WithNoResetAhead_NothingIsJudged(string stored)
    {
        // The last one has passed when the server put the reset off: no end-of-run rule applies until a start reads the next.
        _fixture.Settings.GetRawAsync(ServerReset.Setting, Arg.Any<CancellationToken>()).Returns(stored);
        _fixture.Have(PaybackFixture.Owned("SHUTTLE-1", "SHIP_LIGHT_SHUTTLE", net: -1_000));

        var verdict = await _fixture.Payback().JudgeAsync("SHIP_LIGHT_SHUTTLE", 100_000, PaybackFixture.Now, CancellationToken.None);

        verdict.PaysBack.Should().BeTrue();
        verdict.Judged.Should().BeFalse();
        verdict.ToString().Should().Be("a SHIP_LIGHT_SHUTTLE isn't judged: no reset known");
    }

    [Theory]
    [InlineData("SHIP_PROBE")]
    [InlineData("SHIP_INTERCEPTOR")]
    [InlineData("ship_surveyor")]
    public async Task TheShipsThatEarnNothingThemselves_AreLeftToTheirPlans(string shipType)
    {
        _fixture.ResetIn(hours: 0.5);

        var verdict = await _fixture.Payback().JudgeAsync(shipType, 60_000, PaybackFixture.Now, CancellationToken.None);

        verdict.PaysBack.Should().BeTrue();
        verdict.Judged.Should().BeFalse();
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(30, true)]
    public async Task ADrone_IsJudgedByTheRoleBoardsMiningEstimate_AsTheShuttleSellsItsOre(double hoursLeft, bool paysBack)
    {
        _fixture.ResetIn(hoursLeft);
        _fixture.Have(
            PaybackFixture.Owned("DRONE-1", "SHIP_MINING_DRONE", net: 0),
            PaybackFixture.Owned("DRONE-2", "SHIP_MINING_DRONE", net: 0),
            PaybackFixture.Owned("HAULER-1", "SHIP_LIGHT_HAULER", net: 2_400_000));
        _fixture.Board(
            ("DRONE-1", FleetRole.Mine, 1_000),
            ("DRONE-2", FleetRole.Mine, 2_000),
            ("HAULER-1", FleetRole.Mine, 50_000));

        var verdict = await _fixture.Payback().JudgeAsync("SHIP_MINING_DRONE", 40_000, PaybackFixture.Now, CancellationToken.None);

        verdict.Basis.Should().Be(ShipPayback.FromBoard);
        verdict.CreditsPerHour.Should().Be(1_500, "the drones' own estimates, not the hauler's");
        verdict.PaysBack.Should().Be(paysBack);
    }

    [Fact]
    public async Task ATypeWithLessThanADaysRecord_IsJudgedByTheBoardsTradingEstimate()
    {
        // The first bulk freighter, two hours old, has earned nothing yet: the board's trading estimates stand in.
        _fixture.ResetIn(hours: 30);
        _fixture.Have(
            PaybackFixture.Bought("FREIGHTER-1", "SHIP_BULK_FREIGHTER", net: 0, hoursAgo: 2),
            PaybackFixture.Owned("HAULER-1", "SHIP_LIGHT_HAULER", net: 2_400_000));
        _fixture.Board(("HAULER-1", FleetRole.Trade, 150_000));

        var verdict = await _fixture.Payback().JudgeAsync("SHIP_BULK_FREIGHTER", 3_900_000, PaybackFixture.Now, CancellationToken.None);

        verdict.Basis.Should().Be(ShipPayback.FromBoard);
        verdict.CreditsPerHour.Should().Be(150_000);
        verdict.PaysBack.Should().BeTrue();
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(20, true)]
    public async Task AProbe_PaysBack_WhenItsMarketsPromiseCoversItsPriceFromItsArrival(double hoursLeft, bool paysBack)
    {
        // Slice 6.71 (D151): a market that promises 10,000 an hour pays an 81,645 probe back in 9.2 hours with the
        // first.
        _fixture.ResetIn(hoursLeft);

        var verdict = await _fixture.Payback().JudgeWorthAsync(
            "SHIP_PROBE",
            81_645,
            _ => Task.FromResult(ShipWorth.Of(10_000, "market promise")),
            PaybackFixture.Now,
            CancellationToken.None);

        verdict.PaysBack.Should().Be(paysBack);
        verdict.Decision.Should().Be("D151");
        verdict.ToString().Should().StartWith("a SHIP_PROBE for 81645 earns 10000 an hour by its market promise: 9.2 hours with the first 1, ");
    }

    [Theory]
    [InlineData(1.5, true)]
    [InlineData(0.5, false)]
    public async Task AProbeWhoseWorthIsUnknown_OnlyHasToReachItsWorkBeforeTheReset(double hoursLeft, bool paysBack)
    {
        _fixture.ResetIn(hoursLeft);

        var verdict = await _fixture.Payback().JudgeWorthAsync("SHIP_INTERCEPTOR", 46_663, _ => Task.FromResult(ShipWorth.Unknown), PaybackFixture.Now, CancellationToken.None);

        verdict.PaysBack.Should().Be(paysBack);
        if (!paysBack)
        {
            verdict.ToString().Should().Be("a SHIP_INTERCEPTOR for 46663 can't reach its work before the reset at 2026-11-15 07:30:00Z: 0.5 hours left, its first 1 on its way (D151)");
        }
    }

    [Fact]
    public async Task WithNoResetKnown_AProbesWorthIsNotEvenRead()
    {
        var read = false;

        var verdict = await _fixture.Payback().JudgeWorthAsync(
            "SHIP_PROBE",
            81_645,
            _ =>
            {
                read = true;
                return Task.FromResult(ShipWorth.Unknown);
            },
            PaybackFixture.Now,
            CancellationToken.None);

        verdict.PaysBack.Should().BeTrue();
        read.Should().BeFalse("the promises cost a map read");
    }

    [Fact]
    public async Task ASurveyorsWorth_IsWhatOneDroneOfItsSystemMines_ElseOneDroneAnywhere()
    {
        // Slice 6.71 (D151): "As much as one drone".
        _fixture.Have(
            PaybackFixture.Owned("DRONE-1", "SHIP_MINING_DRONE", net: 0),
            PaybackFixture.Owned("DRONE-2", "SHIP_MINING_DRONE", net: 0),
            (PaybackFixture.Ship("DRONE-3", "SHIP_MINING_DRONE") with { SystemSymbol = "X1-CD" }, 0, DateTimeOffset.MinValue),
            PaybackFixture.Owned("HAULER-1", "SHIP_LIGHT_HAULER", net: 0));
        _fixture.Board(
            ("DRONE-1", FleetRole.Mine, 1_000),
            ("DRONE-2", FleetRole.Mine, 3_000),
            ("DRONE-3", FleetRole.Mine, 8_000),
            ("HAULER-1", FleetRole.Trade, 90_000));
        var payback = _fixture.Payback();

        (await payback.DroneWorthAsync("X1-AB", CancellationToken.None)).Should().Be(ShipWorth.Of(2_000, ShipPayback.FromDrone));
        (await payback.DroneWorthAsync("X1-EF", CancellationToken.None)).Should().Be(ShipWorth.Of(4_000, ShipPayback.FromDrone));
    }

    [Fact]
    public async Task WithNoDroneOnTheBoard_ASurveyorsWorthIsUnknown()
    {
        _fixture.Board(("HAULER-1", FleetRole.Trade, 90_000));

        (await _fixture.Payback().DroneWorthAsync("X1-AB", CancellationToken.None)).Known.Should().BeFalse();
    }

    [Fact]
    public async Task NothingKnownOfTheType_IsNotJudged()
    {
        _fixture.ResetIn(hours: 2);

        var verdict = await _fixture.Payback().JudgeAsync("SHIP_EXPLORER", 700_000, PaybackFixture.Now, CancellationToken.None);

        verdict.PaysBack.Should().BeTrue();
        verdict.ToString().Should().Be("a SHIP_EXPLORER isn't judged: nothing known of what a SHIP_EXPLORER earns");
    }

    [Fact]
    public async Task ATypeThatEarnedNothing_DoesNotPayBack()
    {
        _fixture.ResetIn(hours: 100);
        _fixture.Have(PaybackFixture.Owned("SHUTTLE-1", "SHIP_LIGHT_SHUTTLE", net: -5_000));

        var verdict = await _fixture.Payback().JudgeAsync("SHIP_LIGHT_SHUTTLE", 100_000, PaybackFixture.Now, CancellationToken.None);

        verdict.PaysBack.Should().BeFalse();
        verdict.PaybackHours.Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void ARecord_CountsTheHoursSinceThePurchase_OrTheWindowsStart()
    {
        var from = PaybackFixture.Now - ShipPayback.Window;
        ShipModel[] fleet =
        [
            PaybackFixture.Ship("FREIGHTER-1", "SHIP_BULK_FREIGHTER"),
            PaybackFixture.Ship("FREIGHTER-2", "SHIP_BULK_FREIGHTER"),
            PaybackFixture.Ship("PROBE-1", "SHIP_PROBE"),
        ];

        var records = ShipTypeRecord.Of(
            fleet,
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["freighter-1"] = 1_000, ["FREIGHTER-2"] = 500 },
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase) { ["FREIGHTER-2"] = PaybackFixture.Now.AddHours(-6) },
            from,
            PaybackFixture.Now);

        records["SHIP_BULK_FREIGHTER"].Should().Be(new ShipTypeRecord { Ships = 2, ShipHours = 30, Net = 1_500 });
        records["SHIP_PROBE"].Should().Be(new ShipTypeRecord { Ships = 1, ShipHours = 24, Net = 0 });
    }

    [Fact]
    public async Task TheRecords_AreWorkedOutOnceInFiveMinutes()
    {
        _fixture.ResetIn(hours: 50);
        _fixture.Have(PaybackFixture.Owned("HAULER-1", "SHIP_LIGHT_HAULER", net: 2_400_000));

        await _fixture.Payback().JudgeAsync("SHIP_LIGHT_HAULER", 300_000, PaybackFixture.Now, CancellationToken.None);
        await _fixture.Payback().JudgeAsync("SHIP_BULK_FREIGHTER", 3_900_000, PaybackFixture.Now, CancellationToken.None);

        await _fixture.Ledger.Received(1).GetNetByShipAsync(Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyCollection<LedgerCategory>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AWontPayBack_IsToldAtInformationOnceAnHourForEachPlanAndType()
    {
        _fixture.ResetIn(hours: 5);
        _fixture.Have(PaybackFixture.Owned("SHUTTLE-1", "SHIP_LIGHT_SHUTTLE", net: 0));
        var payback = _fixture.Payback();
        var verdict = await payback.JudgeAsync("SHIP_LIGHT_SHUTTLE", 100_000, PaybackFixture.Now, CancellationToken.None);

        payback.Tell(AutomationPlan.Trading, verdict);
        payback.Tell(AutomationPlan.Trading, verdict);
        payback.Tell(AutomationPlan.Construction, verdict);

        _fixture.Log.Entries.Select(entry => entry.Level).Should().Equal(LogLevel.Information, LogLevel.Debug, LogLevel.Information);
        _fixture.Log.Entries[0].Message.Should().StartWith("Ship purchase: the Trading plan's SHIP_LIGHT_SHUTTLE for 100000 wouldn't earn its price back before the reset at ")
            .And.EndWith("(D150).");
    }
}

/// <summary>A payback check with substitutes (<see cref="ShipPayback"/>); with no reset stored, it judges nothing.</summary>
internal sealed class PaybackFixture
{
    public static readonly DateTimeOffset Now = new(2026, 11, 15, 07, 00, 00, TimeSpan.Zero);

    private readonly List<(ShipModel Ship, long Net, DateTimeOffset BoughtAt)> _fleet = [];

    public PaybackFixture()
    {
        Have();
        Plans.GetAsync<RolePlanState>(PlanTypes.Roles, Arg.Any<CancellationToken>()).Returns((RolePlanState?)null);
    }

    public ISettingsRepository Settings { get; } = Substitute.For<ISettingsRepository>();

    public ILedgerRepository Ledger { get; } = Substitute.For<ILedgerRepository>();

    public IShipRepository Ships { get; } = Substitute.For<IShipRepository>();

    public IPlanRepository Plans { get; } = Substitute.For<IPlanRepository>();

    public MemoryCache Cache { get; } = new(new MemoryCacheOptions());

    public LogRecorder Log { get; } = new();

    /// <summary>A ship of the type, cached by its type.</summary>
    public static ShipModel Ship(string symbol, string shipType) => new(symbol, "X1-AB", "X1-AB-A1", "DOCKED", "CRUISE", 100, 100, ShipType: shipType);

    /// <summary>A ship owned since before the window, which earned <paramref name="net"/> in it.</summary>
    public static (ShipModel Ship, long Net, DateTimeOffset BoughtAt) Owned(string symbol, string shipType, long net)
        => (Ship(symbol, shipType), net, DateTimeOffset.MinValue);

    /// <summary>A ship bought <paramref name="hoursAgo"/> hours ago, which earned <paramref name="net"/> since.</summary>
    public static (ShipModel Ship, long Net, DateTimeOffset BoughtAt) Bought(string symbol, string shipType, long net, double hoursAgo)
        => (Ship(symbol, shipType), net, Now.AddHours(-hoursAgo));

    /// <summary>The next reset, <paramref name="hours"/> from <see cref="Now"/>, to the second (17.7 hours in milliseconds falls short).</summary>
    public void ResetIn(double hours)
        => Settings.GetRawAsync(ServerReset.Setting, Arg.Any<CancellationToken>())
            .Returns(Now.AddSeconds(Math.Round(hours * 3600)).ToString("O", System.Globalization.CultureInfo.InvariantCulture));

    public void Have(params (ShipModel Ship, long Net, DateTimeOffset BoughtAt)[] fleet)
    {
        _fleet.Clear();
        _fleet.AddRange(fleet);
        ShipModel[] ships = [.. _fleet.Select(entry => entry.Ship)];
        Ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(ships);
        IReadOnlyDictionary<string, long> net = _fleet.ToDictionary(entry => entry.Ship.Symbol, entry => entry.Net, StringComparer.OrdinalIgnoreCase);
        Ledger.GetNetByShipAsync(Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyCollection<LedgerCategory>>(), Arg.Any<CancellationToken>()).Returns(net);
        IReadOnlyList<LedgerEntryDto> purchases =
        [
            .. _fleet
                .Where(entry => entry.BoughtAt > DateTimeOffset.MinValue)
                .Select((entry, index) => new LedgerEntryDto(index, entry.BoughtAt, entry.Ship.Symbol, null, nameof(LedgerCategory.ShipPurchase), -1, null, null, null, null)),
        ];
        Ledger.GetRangeAsync().ReturnsForAnyArgs(purchases);
    }

    public void Board(params (string Ship, FleetRole Role, long CreditsPerHour)[] estimates)
        => Plans.GetAsync<RolePlanState>(PlanTypes.Roles, Arg.Any<CancellationToken>()).Returns(new RolePlanState
        {
            EvaluatedAt = Now,
            Ships =
            [
                .. estimates.Select(estimate => new RoleShipState
                {
                    ShipSymbol = estimate.Ship,
                    Role = estimate.Role,
                    Reason = "most_profitable",
                    Since = Now,
                    Estimates = [new RoleEstimateState { Role = estimate.Role, CreditsPerHour = estimate.CreditsPerHour }],
                }),
            ],
        });

    public ShipPayback Payback() => new(new ServerReset(Settings), Ledger, Ships, Plans, Settings, Cache, Log.For<ShipPayback>());

    /// <summary>A payback check on a test's own settings, fleet and role board.</summary>
    public ShipPayback Payback(ISettingsRepository settings, IShipRepository ships, IPlanRepository plans)
        => new(new ServerReset(settings), Ledger, ships, plans, settings, Cache, Log.For<ShipPayback>());

    /// <summary>
    /// The next reset, <paramref name="hours"/> from the clock's now, as a plan that reads the clock judges by it.
    /// </summary>
    public static void ResetFromNow(ISettingsRepository settings, double hours)
        => settings.GetRawAsync(ServerReset.Setting, Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(hours).ToString("O", System.Globalization.CultureInfo.InvariantCulture));
}
