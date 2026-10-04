using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class ShipyardRepositoryTests : IntegrationTestBase
{
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
}
