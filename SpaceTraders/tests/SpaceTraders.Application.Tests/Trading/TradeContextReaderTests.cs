using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// D89: while the system's jump gate needs materials and the construction plan buys them, the trade map names those
/// materials, so the routes that feed the markets making them come first.
/// </summary>
public sealed class TradeContextReaderTests
{
    private const string System = "X1-FJ91";

    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly ISupplyChainCache _chains = Substitute.For<ISupplyChainCache>();
    private readonly IConstructionSites _sites = Substitute.For<IConstructionSites>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();

    public TradeContextReaderTests()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<MarketSnapshot>());
        _waypoints.GetBySystemAsync(System, Arg.Any<CancellationToken>()).Returns(Array.Empty<WaypointCacheModel>());
        _chains.GetAsync(_port, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, IReadOnlyList<string>>());

        // On 2026-10-05 at 15:39Z the home gate had 340 of its 1,600 FAB_MATS and 220 of 400 ADVANCED_CIRCUITRY.
        _sites.CachedNeedingMaterialsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ConstructionSiteModel("X1-FJ91-I64", false, [new ConstructionMaterialModel("FAB_MATS", 1_600, 340), new ConstructionMaterialModel("ADVANCED_CIRCUITRY", 400, 400)]),
            new ConstructionSiteModel("X1-KR90-AF5F", false, [new ConstructionMaterialModel("QUANTUM_STABILIZERS", 1, 0)]),
        ]);
    }

    [Fact]
    public async Task WhileTheGateNeedsMaterials_TheMapNamesThoseOfItsSystemItStillNeeds()
    {
        ConstructionPlanOn(true);

        var context = await Reader().ReadAsync(System, CancellationToken.None);

        context.Map.ConstructionMaterials.Should().BeEquivalentTo(["FAB_MATS"]);
    }

    [Fact]
    public async Task WithTheConstructionPlanOff_TheMapNamesNone()
    {
        ConstructionPlanOn(false);

        var context = await Reader().ReadAsync(System, CancellationToken.None);

        context.Map.ConstructionMaterials.Should().BeEmpty();
        await _sites.DidNotReceiveWithAnyArgs().CachedNeedingMaterialsAsync(default);
    }

    private void ConstructionPlanOn(bool on)
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Construction), Arg.Any<CancellationToken>()).Returns(on);

    private TradeContextReader Reader() => new(_markets, _waypoints, _agents, _settings, _chains, _sites, _port);
}
