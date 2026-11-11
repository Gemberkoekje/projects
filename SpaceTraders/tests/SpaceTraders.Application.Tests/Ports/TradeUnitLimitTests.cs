using System.Net;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Adapters;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.Application.Tests.Ports;

/// <summary>
/// B87: a market takes at most so many units of a good a transaction, and the API refuses more (4604,
/// <c>marketTradeUnitLimitError</c>). The bot sizes a batch by the trade volume it last saw, which can be out of date: the
/// refused batch is traded in parts of the limit the API names, and the caller gets the whole batch's outcome.
/// </summary>
public sealed class TradeUnitLimitTests
{
    private readonly ISpaceTradersApiClient _client = Substitute.For<ISpaceTradersApiClient>();

    [Fact]
    public async Task ASaleAboveTheLimit_IsSoldInPartsOfTheLimit()
    {
        // Seen on the cluster on 2026-11-11 from 05:46:46Z: SPECTER-68 sold 64 LIQUID_NITROGEN at X1-ZZ33-E49, the market's
        // trade volume at 05:46:43Z. The API refused, "a limit of 60 units per transaction", and refused the same 64 on every
        // step after: the market is fetched again only after a sale, so the 64 never changed.
        _client.SellCargoAsync("SPECTER-68", "LIQUID_NITROGEN", 64, Arg.Any<CancellationToken>()).ThrowsAsync(LimitRefusal(60));
        _client.SellCargoAsync("SPECTER-68", "LIQUID_NITROGEN", 60, Arg.Any<CancellationToken>())
            .Returns(Sale(units: 60, totalPrice: 2_280, credits: 1_002_280, unitsLeft: 4));
        _client.SellCargoAsync("SPECTER-68", "LIQUID_NITROGEN", 4, Arg.Any<CancellationToken>())
            .Returns(Sale(units: 4, totalPrice: 148, credits: 1_002_428, unitsLeft: 0));

        var result = await new SpaceTradersPortAdapter(_client).SellCargoAsync("SPECTER-68", "LIQUID_NITROGEN", 64);

        result.Revenue.Should().Be(2_428);
        result.AgentCredits.Should().Be(1_002_428);
        result.Cargo.Units.Should().Be(0);
        Received.InOrder(() =>
        {
            _client.SellCargoAsync("SPECTER-68", "LIQUID_NITROGEN", 64, Arg.Any<CancellationToken>());
            _client.SellCargoAsync("SPECTER-68", "LIQUID_NITROGEN", 60, Arg.Any<CancellationToken>());
            _client.SellCargoAsync("SPECTER-68", "LIQUID_NITROGEN", 4, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task APurchaseAboveTheLimit_IsBoughtInPartsOfTheLimit_AsManyAsItTakes()
    {
        _client.BuyCargoAsync("SPECTER-3", "LIQUID_NITROGEN", 130, Arg.Any<CancellationToken>()).ThrowsAsync(LimitRefusal(60));
        _client.BuyCargoAsync("SPECTER-3", "LIQUID_NITROGEN", 60, Arg.Any<CancellationToken>())
            .Returns(Purchase(units: 60, totalPrice: 3_720, credits: 96_280, unitsHeld: 60), Purchase(units: 60, totalPrice: 3_900, credits: 92_380, unitsHeld: 120));
        _client.BuyCargoAsync("SPECTER-3", "LIQUID_NITROGEN", 10, Arg.Any<CancellationToken>())
            .Returns(Purchase(units: 10, totalPrice: 660, credits: 91_720, unitsHeld: 130));

        var result = await new SpaceTradersPortAdapter(_client).BuyCargoAsync("SPECTER-3", "LIQUID_NITROGEN", 130);

        result.Revenue.Should().Be(8_280);
        result.AgentCredits.Should().Be(91_720);
        result.Cargo.Units.Should().Be(130);
        await _client.Received(2).BuyCargoAsync("SPECTER-3", "LIQUID_NITROGEN", 60, Arg.Any<CancellationToken>());
        await _client.Received(1).BuyCargoAsync("SPECTER-3", "LIQUID_NITROGEN", 10, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Market transaction failed. Trade good LIQUID_NITROGEN has a limit on units per transaction.")]
    [InlineData("Market transaction failed. Trade good LIQUID_NITROGEN has a limit of 64 units per transaction.")]
    [InlineData("Market transaction failed. Trade good LIQUID_NITROGEN has a limit of 0 units per transaction.")]
    public async Task ALimitRefusalWithoutALowerLimitToTradeAt_StaysTheApis(string apiMessage)
    {
        // No number, or none that is smaller than the batch: trading in parts would guess, or try the same again.
        _client.SellCargoAsync("SPECTER-68", "LIQUID_NITROGEN", 64, Arg.Any<CancellationToken>())
            .ThrowsAsync(Refusal(4604, apiMessage));

        var sell = () => new SpaceTradersPortAdapter(_client).SellCargoAsync("SPECTER-68", "LIQUID_NITROGEN", 64);

        await sell.Should().ThrowAsync<SpaceTradersApiException>();
        await _client.Received(1).SellCargoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnyOtherRefusal_StaysTheApis()
    {
        // 4600: too few credits. Its message has no limit in it, but a smaller purchase isn't what the caller asked for.
        _client.BuyCargoAsync("SPECTER-3", "LIQUID_NITROGEN", 130, Arg.Any<CancellationToken>())
            .ThrowsAsync(Refusal(4600, "Market transaction failed. Agent does not have sufficient credits to purchase 130 unit(s) of LIQUID_NITROGEN"));

        var buy = () => new SpaceTradersPortAdapter(_client).BuyCargoAsync("SPECTER-3", "LIQUID_NITROGEN", 130);

        await buy.Should().ThrowAsync<SpaceTradersApiException>();
        await _client.Received(1).BuyCargoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The refusal as the API client raises it: its message ends with what the API said.</summary>
    private static SpaceTradersApiException LimitRefusal(int limit)
        => Refusal(4604, $"Market transaction failed. Trade good LIQUID_NITROGEN has a limit of {limit} units per transaction.");

    private static SpaceTradersApiException Refusal(int errorCode, string apiMessage)
        => new(
            $"SpaceTraders API request to 'my/ships/SPECTER-68/sell' failed with status code 400: {apiMessage}",
            HttpStatusCode.BadRequest,
            "my/ships/SPECTER-68/sell",
            JsonSerializer.Serialize(new { error = new { message = apiMessage, code = errorCode } }),
            errorCode);

    private static SellCargoResult Sale(int units, long totalPrice, long credits, int unitsLeft)
        => new()
        {
            Agent = new Agent { Symbol = "SPECTER", StartingFaction = "COBALT", Credits = credits },
            Cargo = Hold(unitsLeft),
            Transaction = Transaction("SELL", units, totalPrice),
        };

    private static BuyCargoResult Purchase(int units, long totalPrice, long credits, int unitsHeld)
        => new()
        {
            Agent = new Agent { Symbol = "SPECTER", StartingFaction = "COBALT", Credits = credits },
            Cargo = Hold(unitsHeld),
            Transaction = Transaction("PURCHASE", units, totalPrice),
        };

    private static ShipCargo Hold(int units)
        => new() { Capacity = 160, Units = units, Inventory = units > 0 ? [new CargoItem { Symbol = "LIQUID_NITROGEN", Units = units }] : [] };

    private static MarketTransaction Transaction(string type, int units, long totalPrice)
        => new()
        {
            WaypointSymbol = "X1-ZZ33-E49",
            ShipSymbol = "SPECTER-68",
            TradeSymbol = "LIQUID_NITROGEN",
            Type = type,
            Units = units,
            PricePerUnit = totalPrice / units,
            TotalPrice = totalPrice,
            Timestamp = DateTimeOffset.UtcNow,
        };
}
