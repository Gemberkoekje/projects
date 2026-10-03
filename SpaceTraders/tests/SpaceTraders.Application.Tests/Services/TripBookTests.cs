using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// D46 (2026-10-03): "I'd like to see the actual trade profits, so the actual sell - buy - fuel". Each trip is booked when
/// it ends: what its sales brought in, less what its cargo cost and the fuel its ship bought since it started; a contract
/// round trip at its delivery, as the fuel its ship bought since it was assigned.
/// </summary>
public sealed class TripBookTests
{
    private readonly ILedgerRepository _ledger = Substitute.For<ILedgerRepository>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly LogRecorder _log = new();

    /// <summary>Each kind of trip, with the activity it is booked under.</summary>
    public static TheoryData<TripGoal, string> Activities => new()
    {
        { new TradeBetweenMarketsGoal { TradeSymbol = "EQUIPMENT", BuyWaypointSymbol = "X1-AB-K85", SellWaypointSymbol = "X1-AB-D41" }, "trade" },
        { new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = "X1-AB-XB5C", SellWaypointSymbol = "X1-AB-H51" }, "mining" },
        { new SiphonAndSellGoal { TradeSymbol = "LIQUID_HYDROGEN", SourceWaypointSymbol = "X1-AB-C38", SellWaypointSymbol = "X1-AB-G50" }, "siphoning" },
        { new GatherAndSellGoal { SourceWaypointSymbol = "X1-AB-XB5C" }, "spare_time" },
    };

    [Fact]
    public async Task ATrip_MakesWhatItsSalesBroughtIn_LessWhatItsCargoCost_AndTheFuelItsShipBoughtSinceItStarted()
    {
        var startedAt = TimeProvider.System.GetUtcNow().AddMinutes(-42);
        FuelBought("SPECTER-1", startedAt, 1_200, 2_380);

        await Book().BookAsync("SPECTER-1", Trade(startedAt) with { Earned = 109_220, Spent = 101_320 }, TripBook.Sold, CancellationToken.None);

        var line = _log.Journal.Should().ContainSingle().Subject;
        line.EventKind.Should().Be("TripEnded");
        line.Level.Should().Be(LogLevel.Information);
        line.Message.Should().Be("TripEnded: ship SPECTER-1 made 4320 credits on its trade trip in 42 minutes: sold for 109220, bought for 101320, fuel 3580 (sold).");
        line.Properties["ShipSymbol"].Should().Be("SPECTER-1");
        line.Properties["Activity"].Should().Be("trade");
        line.Properties["Earned"].Should().Be(109_220L);
        line.Properties["Spent"].Should().Be(101_320L);
        line.Properties["FuelCost"].Should().Be(3_580L);
        line.Properties["Profit"].Should().Be(4_320L);
        line.Properties["Minutes"].Should().Be(42);
        line.Properties["Reason"].Should().Be("sold");
        _metrics.Received(1).TripEnded("trade");
        _metrics.Received(1).TripProfit("trade", 4_320);
    }

    [Fact]
    public async Task ATripThatCostMoreThanItBroughtIn_IsALoss()
    {
        // A trade dropped at its sell market: the cargo was bought, and the plan sells it on a trip of its own.
        var startedAt = TimeProvider.System.GetUtcNow().AddMinutes(-25);
        FuelBought("SPECTER-1", startedAt, 152);

        await Book().BookAsync("SPECTER-1", Trade(startedAt) with { Spent = 65_080 }, "not_bought_here", CancellationToken.None);

        _metrics.Received(1).TripProfit("trade", -65_232);
        var line = _log.Journal.Should().ContainSingle().Subject;
        line.Properties["Profit"].Should().Be(-65_232L);
        line.Properties["Reason"].Should().Be("not_bought_here");
    }

    [Theory]
    [MemberData(nameof(Activities))]
    public async Task EachKindOfTrip_IsBookedUnderItsActivity(TripGoal trip, string activity)
    {
        await Book().BookAsync("SHIP-3", trip, TripBook.Sold, CancellationToken.None);

        _metrics.Received(1).TripEnded(activity);
        _metrics.Received(1).TripProfit(activity, 0);
        _log.Journal.Should().ContainSingle().Which.Properties["Activity"].Should().Be(activity);
    }

    [Fact]
    public async Task AContractRoundTrip_IsBookedAtItsDelivery_AsTheFuelItsShipBoughtSinceItWasAssigned()
    {
        // The contract's deposit and payout count as its profit when they come (LedgerEntryHandler).
        var assignedAt = TimeProvider.System.GetUtcNow().AddMinutes(-31);
        FuelBought("SHIP-6", assignedAt, 350);

        await Book().BookContractTripAsync("SHIP-6", assignedAt, CancellationToken.None);

        _metrics.Received(1).TripEnded("contract");
        _metrics.Received(1).TripProfit("contract", -350);
        var line = _log.Journal.Should().ContainSingle().Subject;
        line.EventKind.Should().Be("TripEnded");
        line.Properties["ShipSymbol"].Should().Be("SHIP-6");
        line.Properties["Activity"].Should().Be("contract");
        line.Properties["Earned"].Should().Be(0L);
        line.Properties["Spent"].Should().Be(0L);
        line.Properties["FuelCost"].Should().Be(350L);
        line.Properties["Profit"].Should().Be(-350L);
        line.Properties["Minutes"].Should().Be(31);
        line.Properties["Reason"].Should().Be("delivered");
    }

    private static TradeBetweenMarketsGoal Trade(DateTimeOffset startedAt) => new()
    {
        TradeSymbol = "EQUIPMENT",
        BuyWaypointSymbol = "X1-AB-K85",
        SellWaypointSymbol = "X1-AB-D41",
        StartedAt = startedAt,
    };

    /// <summary>The ship's fuel purchases in the ledger, as the trip book asks for them: from the trip's start on.</summary>
    private void FuelBought(string shipSymbol, DateTimeOffset since, params long[] costs)
        => _ledger.GetRangeAsync(since, Arg.Any<DateTimeOffset?>(), shipSymbol, LedgerCategory.FuelPurchase, Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(costs
                .Select((cost, index) => new LedgerEntryDto(index + 1, since.AddMinutes(index + 1), shipSymbol, null, nameof(LedgerCategory.FuelPurchase), -cost, null, null, null, "X1-AB-K85"))
                .ToList());

    private TripBook Book() => new(_ledger, _metrics, _log.For<TripBook>());
}
