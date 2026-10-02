using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// The production chains are fetched once per process and shared by the trading plan and the markets
/// dashboard (slices 6.5 and 2.8); after a failure the next try waits an hour.
/// </summary>
public sealed class SupplyChainCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 02, 12, 00, 00, TimeSpan.Zero);

    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();

    [Fact]
    public async Task TheChains_AreFetchedOnce()
    {
        _port.GetSupplyChainAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, IReadOnlyList<string>> { ["SHIP_PARTS"] = ["ELECTRONICS", "EQUIPMENT"] });
        using var cache = new SupplyChainCache(NullLogger<SupplyChainCache>.Instance);

        var first = await cache.GetAsync(_port, Start, CancellationToken.None);
        var second = await cache.GetAsync(_port, Start.AddHours(5), CancellationToken.None);

        first["ship_parts"].Should().Equal("ELECTRONICS", "EQUIPMENT");
        second.Should().BeSameAs(first);
        await _port.Received(1).GetSupplyChainAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterAFailure_TheChainsAreUnknown_UntilTheNextTryAnHourLater()
    {
        _port.GetSupplyChainAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("502 Bad Gateway"));
        using var cache = new SupplyChainCache(NullLogger<SupplyChainCache>.Instance);

        (await cache.GetAsync(_port, Start, CancellationToken.None)).Should().BeEmpty();
        (await cache.GetAsync(_port, Start.AddMinutes(59), CancellationToken.None)).Should().BeEmpty();
        await _port.Received(1).GetSupplyChainAsync(Arg.Any<CancellationToken>());

        await cache.GetAsync(_port, Start.AddHours(1), CancellationToken.None);
        await _port.Received(2).GetSupplyChainAsync(Arg.Any<CancellationToken>());
    }
}
