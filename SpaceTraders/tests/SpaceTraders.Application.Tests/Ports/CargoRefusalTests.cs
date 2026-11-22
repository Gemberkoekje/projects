using System.Net;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Adapters;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;

namespace SpaceTraders.Application.Tests.Ports;

/// <summary>
/// B95: a sale the API carried out but whose answer the bot lost leaves the bot's cargo ahead of the ship's, and the API
/// refuses the next sale of those units. The port says so with a <see cref="CargoRefusedException"/>, so the goal step can
/// read the cargo again instead of sending the same sale on every step.
/// </summary>
public sealed class CargoRefusalTests
{
    private const string ShortMessage =
        "Failed to update ship cargo. Ship SPECTER-1F4 cargo does not contain 45 unit(s) of AMMUNITION. Ship has 40 unit(s) of AMMUNITION.";

    private readonly ISpaceTradersApiClient _client = Substitute.For<ISpaceTradersApiClient>();

    [Fact]
    public async Task ASaleOfMoreUnitsThanTheShipHolds_IsACargoRefusal()
    {
        // Seen on the cluster from 2026-11-21 23:11Z: SPECTER-1F4's tenth sale of 45 AMMUNITION at X1-V44-CA1C was cancelled
        // by Wolverine's 60-second limit while the API carried it out. The bot still counted 85 aboard, the API 40, and it
        // refused the same 45 on every step for 11 hours.
        _client.SellCargoAsync("SPECTER-1F4", "AMMUNITION", 45, Arg.Any<CancellationToken>()).ThrowsAsync(Refusal(ShortMessage));

        var sell = () => new SpaceTradersPortAdapter(_client).SellCargoAsync("SPECTER-1F4", "AMMUNITION", 45);

        var refused = (await sell.Should().ThrowAsync<CargoRefusedException>()).Which;
        refused.ShipSymbol.Should().Be("SPECTER-1F4");
        refused.TradeSymbol.Should().Be("AMMUNITION");
        refused.Units.Should().Be(45);
        refused.InnerException.Should().BeOfType<SpaceTradersApiException>();
    }

    [Fact]
    public async Task ACargoRefusalThatDoesNotSayHowManyTheShipHolds_IsOneAllTheSame()
    {
        _client.SellCargoAsync("SPECTER-1F4", "AMMUNITION", 45, Arg.Any<CancellationToken>())
            .ThrowsAsync(Refusal("Failed to update ship cargo. Ship SPECTER-1F4 cargo does not contain AMMUNITION."));

        var sell = () => new SpaceTradersPortAdapter(_client).SellCargoAsync("SPECTER-1F4", "AMMUNITION", 45);

        await sell.Should().ThrowAsync<CargoRefusedException>();
    }

    [Fact]
    public async Task AnyOtherRefusalOfASale_StaysTheApis()
    {
        _client.SellCargoAsync("SPECTER-1F4", "AMMUNITION", 45, Arg.Any<CancellationToken>())
            .ThrowsAsync(Refusal("Market sell failed. Trade good AMMUNITION is not available at X1-V44-CA1C."));

        var sell = () => new SpaceTradersPortAdapter(_client).SellCargoAsync("SPECTER-1F4", "AMMUNITION", 45);

        await sell.Should().ThrowAsync<SpaceTradersApiException>();
    }

    /// <summary>The refusal as the API client raises it: its message ends with what the API said.</summary>
    private static SpaceTradersApiException Refusal(string apiMessage)
        => new(
            $"SpaceTraders API request to 'my/ships/SPECTER-1F4/sell' failed with status code 400: {apiMessage}",
            HttpStatusCode.BadRequest,
            "my/ships/SPECTER-1F4/sell",
            JsonSerializer.Serialize(new { error = new { message = apiMessage } }));
}
