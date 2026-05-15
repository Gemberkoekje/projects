using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class ShipyardRepositoryTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task FindShipyardForTypeAsync_OnlyReturnsShipyardForCurrentAgentToken()
    {
        var repo = new ShipyardRepository(Db);

        await repo.UpsertAsync(new ShipyardDataModel(
            WaypointSymbol: "X1-OWN-H53",
            SystemSymbol: "X1-OWN",
            ShipTypesJson: "[\"SHIP_MINING_DRONE\"]",
            ShipsDetailJson: "[{\"type\":\"SHIP_MINING_DRONE\",\"purchasePrice\":39716}]"));

        Db.Shipyards.Add(new CachedShipyard
        {
            AgentToken = "other-agent-token",
            WaypointSymbol = "X1-OTHER-H53",
            SystemSymbol = "X1-OTHER",
            ShipTypesJson = "[\"SHIP_MINING_DRONE\"]",
            ShipsDetailJson = "[{\"type\":\"SHIP_MINING_DRONE\",\"purchasePrice\":1000}]",
            LastObservedAt = DateTimeOffset.UtcNow.AddMinutes(5),
        });
        await Db.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        var freshRepo = new ShipyardRepository(fresh);

        var waypoint = await freshRepo.FindShipyardForTypeAsync("SHIP_MINING_DRONE");

        waypoint.Should().Be("X1-OWN-H53");
    }
}
