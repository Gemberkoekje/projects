using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class ShipyardRepositoryTests : IntegrationTestBase
{
    private const string Yard = "X1-KR90-YARD";
    private const string SystemSymbol = "X1-KR90";

    [SkippableFact]
    public async Task FindShipyardForTypeAsync_OnlyReturnsShipyardForCurrentAgent()
    {
        var repo = new ShipyardRepository(Db);

        await repo.UpsertAsync(new ShipyardDataModel(
            WaypointSymbol: "X1-OWN-H53",
            SystemSymbol: "X1-OWN",
            ShipTypesJson: "[\"SHIP_MINING_DRONE\"]",
            ShipsDetailJson: "[{\"type\":\"SHIP_MINING_DRONE\",\"purchasePrice\":39716}]"));

        Db.Shipyards.Add(new CachedShipyard
        {
            AgentId = "OTHER-AGENT@2026-09-27",
            WaypointSymbol = "X1-OTHER-H53",
            SystemSymbol = "X1-OTHER",
            ShipTypesJson = "[\"SHIP_MINING_DRONE\"]",
            ShipsDetailJson = "[{\"type\":\"SHIP_MINING_DRONE\",\"purchasePrice\":1000}]",
            LastObservedAt = DateTimeOffset.UtcNow.AddMinutes(5),
        });
        await Db.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        var freshRepo = new ShipyardRepository(fresh);

        var waypoint = await freshRepo.FindShipyardForTypeAsync("SHIP_MINING_DRONE", ["X1-OWN", "X1-OTHER"]);

        waypoint.Should().Be("X1-OWN-H53");
    }

    [SkippableFact]
    public async Task FindShipyardForTypeAsync_OnlyLooksInTheSystemsItIsGiven()
    {
        // Asked on 2026-10-04: the shipyard seen last can be one the command ship explored; business stays where our ships work.
        var repo = new ShipyardRepository(Db);
        await repo.UpsertAsync(new ShipyardDataModel("X1-OWN-H53", "X1-OWN", "[\"SHIP_MINING_DRONE\"]"));
        await repo.UpsertAsync(new ShipyardDataModel("X1-KR90-YARD", "X1-KR90", "[\"SHIP_MINING_DRONE\"]"));

        await using var fresh = CreateFreshContext();
        var waypoint = await new ShipyardRepository(fresh).FindShipyardForTypeAsync("SHIP_MINING_DRONE", ["X1-OWN"]);

        waypoint.Should().Be("X1-OWN-H53", "X1-KR90's was seen last, but isn't where our ships work");
    }

    /// <summary>
    /// B61: a shipyard was stored as a market was, so two writers storing a shipyard nobody had fetched at the same moment
    /// both inserted it, and the second failed on PK_cached_shipyards (23505). That takes two ships there at once: two
    /// arrivals, or an arrival where the exploring command ship stores the shipyard. Here one arrival stores the shipyard
    /// between the other's read and its write.
    /// </summary>
    [SkippableFact]
    public async Task UpsertAsync_AShipyardAnotherArrivalStoredMeanwhile_IsUpdated()
    {
        await using var firstArrival = CreateFreshContext();
        var firstArrivalStoresFirst = new BeforeInsertInterceptor(
            "cached_shipyards",
            async () => await new ShipyardRepository(firstArrival).UpsertAsync(Shipyard(dronePrice: 46_885)));
        await using var secondArrival = CreateFreshContext(firstArrivalStoresFirst);

        await new ShipyardRepository(secondArrival).UpsertAsync(Shipyard(dronePrice: 47_012));

        firstArrivalStoresFirst.HasRun.Should().BeTrue("the first arrival stored the shipyard first");
        await using var fresh = CreateFreshContext();
        var shipyard = await new ShipyardRepository(fresh).FindByWaypointAsync(Yard);
        shipyard.Should().NotBeNull();
        shipyard.Ships.Should().ContainSingle().Which.PurchasePrice.Should().Be(47_012, "the second arrival's fetch went to the database last");
    }

    [SkippableFact]
    public async Task UpsertAsync_StoresTheShipyardAsFetched_AndTheNextFetchReplacesIt()
    {
        var shipyards = new ShipyardRepository(Db);
        await shipyards.UpsertAsync(new ShipyardDataModel(Yard, SystemSymbol, """[{"type":"SHIP_MINING_DRONE"}]""", Ships(46_885)));
        var first = await StoredAsync();

        await shipyards.UpsertAsync(new ShipyardDataModel(Yard, SystemSymbol, """[{"type":"SHIP_PROBE"}]""", Ships(47_012)));
        var second = await StoredAsync();

        first.Should().BeEquivalentTo(new
        {
            SystemSymbol,
            ShipTypesJson = """[{"type":"SHIP_MINING_DRONE"}]""",
            ShipsDetailJson = Ships(46_885),
        });
        second.Should().BeEquivalentTo(new
        {
            SystemSymbol,
            ShipTypesJson = """[{"type":"SHIP_PROBE"}]""",
            ShipsDetailJson = Ships(47_012),
        });
        second.LastObservedAt.Should().BeAfter(first.LastObservedAt);
    }

    /// <summary>
    /// B64: the API lists a shipyard's ships, with their prices, only while one of our ships is there; another answer has
    /// only the ship types. The shipyard was stored as it came, so such an answer wiped the listings the cache had, and with
    /// them the prices a purchase reads (B28), as an answer without prices did to a market's (B62).
    /// </summary>
    [SkippableFact]
    public async Task UpsertAsync_AnAnswerWithoutListings_KeepsTheCachedOnes()
    {
        var shipyards = new ShipyardRepository(Db);
        await shipyards.UpsertAsync(new ShipyardDataModel(Yard, SystemSymbol, """[{"type":"SHIP_MINING_DRONE"}]""", Ships(46_885)));
        var listed = await StoredAsync();

        await shipyards.UpsertAsync(new ShipyardDataModel(Yard, SystemSymbol, """[{"type":"SHIP_MINING_DRONE"}]"""));

        var stored = await StoredAsync();
        stored.ShipsDetailJson.Should().Be(Ships(46_885), "an answer without listings says nothing about them");
        stored.LastObservedAt.Should().Be(listed.LastObservedAt, "the listings weren't seen again");
    }

    [SkippableFact]
    public async Task UpsertAsync_AnAnswerWithoutListings_IsStoredWhenTheCacheHasNone()
    {
        // A shipyard no ship of ours has been at: its ship types are all there is to keep, and the time they were seen.
        var shipyards = new ShipyardRepository(Db);
        await shipyards.UpsertAsync(new ShipyardDataModel(Yard, SystemSymbol, """[{"type":"SHIP_MINING_DRONE"}]"""));
        var first = await StoredAsync();

        await shipyards.UpsertAsync(new ShipyardDataModel(Yard, SystemSymbol, """[{"type":"SHIP_MINING_DRONE"},{"type":"SHIP_PROBE"}]"""));

        var stored = await StoredAsync();
        stored.ShipTypesJson.Should().Be("""[{"type":"SHIP_MINING_DRONE"},{"type":"SHIP_PROBE"}]""");
        stored.ShipsDetailJson.Should().BeNull();
        stored.LastObservedAt.Should().BeAfter(first.LastObservedAt);
    }

    private static ShipyardDataModel Shipyard(long dronePrice) => new(Yard, SystemSymbol, """[{"type":"SHIP_MINING_DRONE"}]""", Ships(dronePrice));

    private static string Ships(long dronePrice) => $$"""[{"type":"SHIP_MINING_DRONE","purchasePrice":{{dronePrice}},"supply":"MODERATE"}]""";

    private async Task<CachedShipyard> StoredAsync()
    {
        await using var fresh = CreateFreshContext();
        return await fresh.Shipyards.AsNoTracking().SingleAsync(s => s.WaypointSymbol == Yard);
    }
}
