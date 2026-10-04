using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using static SpaceTraders.Application.Tests.Construction.ConstructionFixture;

namespace SpaceTraders.Application.Tests.Construction;

/// <summary>
/// Slice 6.6: the construction sites as the plan knows them. Only the home system's jump gate counts (D68); it is fetched when
/// first seen under construction and again every 10 minutes while it needs materials, stored from every supply's answer, and
/// a site that starts or stops needing materials is journaled.
/// </summary>
public sealed class ConstructionSitesTests : IDisposable
{
    private readonly Infrastructure.Persistence.SpaceTradersDbContext _db = TestDbContextFactory.Create();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ConstructionSiteWatch _watch = new();
    private readonly LogRecorder _log = new();

    public ConstructionSitesTests()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, "X1-DC53-A1", 175_000, "COSMIC", 2));
        _waypoints.GetBySystemAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Waypoints);
        _port.GetConstructionSiteAsync(SystemSymbol, Gate, Arg.Any<CancellationToken>()).Returns(Site(fabMats: 120));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task TheHomeSystem_IsTheSystemOfTheHeadquarters()
    {
        (await Sites().HomeSystemAsync(CancellationToken.None)).Should().Be(SystemSymbol);

        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns((AgentModel?)null);
        (await Sites().HomeSystemAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task AGateFirstSeenUnderConstruction_IsFetched_Stored_AndJournaled()
    {
        var sites = await Sites().NeedingMaterialsAsync(CancellationToken.None);

        sites.Should().ContainSingle().Which.Materials.Single(material => material.TradeSymbol == "FAB_MATS").Fulfilled.Should().Be(120);
        (await new ConstructionRepository(_db).FindAsync(Gate)).Should().NotBeNull();
        var started = _log.Journal.Should().ContainSingle().Subject;
        started.EventKind.Should().Be("PlanStarted");
        started.Properties["Materials"].Should().Be("FAB_MATS 120/1600, ADVANCED_CIRCUITRY 0/400, QUANTUM_STABILIZERS 1/1");
    }

    [Fact]
    public async Task WithinTenMinutes_TheSiteIsntFetchedAgain()
    {
        await Sites().NeedingMaterialsAsync(CancellationToken.None);
        await Sites().NeedingMaterialsAsync(CancellationToken.None);

        await _port.Received(1).GetConstructionSiteAsync(SystemSymbol, Gate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterTenMinutes_TheSiteIsFetchedAgain_AsOtherAgentsSupplyItToo()
    {
        await Sites().NeedingMaterialsAsync(CancellationToken.None);
        _watch.Attempted(Gate, DateTimeOffset.UtcNow - ConstructionSiteWatch.Interval);
        _port.GetConstructionSiteAsync(SystemSymbol, Gate, Arg.Any<CancellationToken>()).Returns(Site(fabMats: 600));

        var sites = await Sites().NeedingMaterialsAsync(CancellationToken.None);

        sites.Single().Materials.Single(material => material.TradeSymbol == "FAB_MATS").Fulfilled.Should().Be(600);
    }

    [Fact]
    public async Task ACompleteGate_IsJournaled_AndNeverFetchedAgain()
    {
        await Sites().NeedingMaterialsAsync(CancellationToken.None);
        _watch.Attempted(Gate, DateTimeOffset.UtcNow - ConstructionSiteWatch.Interval);
        _port.GetConstructionSiteAsync(SystemSymbol, Gate, Arg.Any<CancellationToken>()).Returns(Site(fabMats: 1_600, circuitry: 400, complete: true));

        (await Sites().NeedingMaterialsAsync(CancellationToken.None)).Should().BeEmpty();
        _watch.Attempted(Gate, DateTimeOffset.UtcNow - ConstructionSiteWatch.Interval);
        (await Sites().NeedingMaterialsAsync(CancellationToken.None)).Should().BeEmpty();

        await _port.Received(2).GetConstructionSiteAsync(SystemSymbol, Gate, Arg.Any<CancellationToken>());
        _log.Journal.Select(entry => entry.EventKind).Should().Equal("PlanStarted", "PlanCompleted");
    }

    [Fact]
    public async Task AGateTheWaypointCacheSaysIsBuilt_CostsNoCall()
    {
        // X1-DC53-I55 on 2026-10-04: complete before our ships came.
        _waypoints.GetBySystemAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(
            [.. Waypoints.Select(waypoint => waypoint.Symbol == Gate ? waypoint with { IsUnderConstruction = false } : waypoint)]);

        (await Sites().NeedingMaterialsAsync(CancellationToken.None)).Should().BeEmpty();

        await _port.DidNotReceiveWithAnyArgs().GetConstructionSiteAsync(default!, default!, default);
    }

    [Fact]
    public async Task OnlyTheHomeSystemsGates_AreLookedAt()
    {
        // D68: a gate under construction elsewhere isn't considered.
        await Sites().NeedingMaterialsAsync(CancellationToken.None);

        await _waypoints.Received(1).GetBySystemAsync(SystemSymbol, Arg.Any<CancellationToken>());
        await _waypoints.DidNotReceive().GetBySystemAsync(Arg.Is<string>(system => system != SystemSymbol), Arg.Any<CancellationToken>());

        await new ConstructionRepository(_db).UpsertAsync(Site() with { WaypointSymbol = "X1-HZ59-I59" }, "X1-HZ59");
        (await Sites().CachedNeedingMaterialsAsync(CancellationToken.None)).Select(site => site.WaypointSymbol).Should().Equal(Gate);
    }

    [Fact]
    public async Task AFailedFetch_IsLoggedAndWaitsItsTurn()
    {
        _port.GetConstructionSiteAsync(SystemSymbol, Gate, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("502"));

        (await Sites().NeedingMaterialsAsync(CancellationToken.None)).Should().BeEmpty();
        (await Sites().NeedingMaterialsAsync(CancellationToken.None)).Should().BeEmpty();

        await _port.Received(1).GetConstructionSiteAsync(SystemSymbol, Gate, Arg.Any<CancellationToken>());
        _log.Entries.Should().ContainSingle(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    private ConstructionSites Sites()
        => new(new ConstructionRepository(_db), _waypoints, _agents, _port, _watch, _log.For<ConstructionSites>());
}
