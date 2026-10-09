using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>
/// Slice 6.40 (D122), asked on 2026-10-09: "I'd like mining to be done wherever there's low ore supply, not just in the home
/// area." Abroad, only the markets the probes keep fresh count (D96): a market whose prices are older than
/// <c>Trade.MaxPriceAgeMinutes</c> is stale there. At home every market counts, as before.
/// </summary>
public sealed class MiningContextReaderTests
{
    private readonly ITradeContextReader _trade = Substitute.For<ITradeContextReader>();
    private readonly ISurveyRepository _surveys = Substitute.For<ISurveyRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();

    public MiningContextReaderTests()
    {
        HomeIs("X1-KR90-A1");
        _trade.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(Map(), 129_357, 200));
        _trade.FreshMarketsAsync(Arg.Any<CancellationToken>()).Returns(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { XB5C, F49, H52, "X1-KR90-A1", "X1-AB12-C3" });
        _surveys.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<StoredSurvey>());
    }

    [Fact]
    public async Task Abroad_OnlyTheMarketsTheProbesKeepFreshCount()
    {
        // X1-DC53 is abroad: H51 and B7 were last seen too long ago, so the drone mines for F49 alone.
        var context = await Reader().ReadAsync(SystemSymbol, CancellationToken.None);

        context.Map.StaleMarkets.Should().BeEquivalentTo([H51, B7]);
        MiningPlanner.MiningTargets(context, Drone(), new HashSet<string>()).Select(target => target.SellWaypointSymbol).Distinct().Should().Equal(F49);
    }

    [Fact]
    public async Task AtHome_EveryMarketCounts_AsBefore()
    {
        HomeIs($"{SystemSymbol}-A1");

        var context = await Reader().ReadAsync(SystemSymbol, CancellationToken.None);

        context.Map.StaleMarkets.Should().BeEmpty();
        await _trade.DidNotReceiveWithAnyArgs().FreshMarketsAsync(default);
    }

    [Fact]
    public async Task TheSystemsAbroad_AreThoseWithAFreshMarket_ButHome()
        => (await Reader().SystemsAbroadAsync(CancellationToken.None)).Should().Equal("X1-AB12", SystemSymbol);

    [Fact]
    public async Task WhileHomeIsntKnown_NoSystemIsAbroad()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns((AgentModel?)null);

        (await Reader().SystemsAbroadAsync(CancellationToken.None)).Should().BeEmpty();
        (await Reader().ReadAsync(SystemSymbol, CancellationToken.None)).Map.StaleMarkets.Should().BeEmpty();
    }

    private void HomeIs(string headquarters)
        => _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, headquarters, 1_000_000, "COBALT", 3));

    private MiningContextReader Reader() => new(_trade, _surveys, _agents);
}
