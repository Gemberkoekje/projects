using FluentAssertions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class PlanRepositoryTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task UpsertAsync_AndGetAsync_ScoutPlan_RoundTrips()
    {
        var state = new ScoutAllMarketplacesPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = "SHIP-1",
            StartWaypointSymbol = "X1-START",
            RouteWaypointSymbols = ["X1-START", "X1-MARKET-1"],
            CurrentRouteIndex = 1,
            Status = ScoutPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var repo = new PlanRepository(Db);
        await repo.UpsertAsync(PlanTypes.ScoutAllMarketplaces, state);

        await using var fresh = CreateFreshContext();
        var readRepo = new PlanRepository(fresh);
        var roundTrip = await readRepo.GetAsync<ScoutAllMarketplacesPlanState>(PlanTypes.ScoutAllMarketplaces);

        roundTrip.Should().NotBeNull();
        roundTrip!.PlanId.Should().Be(state.PlanId);
        roundTrip.ShipSymbol.Should().Be("SHIP-1");
        roundTrip.RouteWaypointSymbols.Should().Equal("X1-START", "X1-MARKET-1");
        roundTrip.CurrentRouteIndex.Should().Be(1);
        roundTrip.Status.Should().Be(ScoutPlanStatus.Active);
    }

    [SkippableFact]
    public async Task UpsertAsync_AndGetAsync_DifferentPlanType_RoundTripsDifferentPayload()
    {
        var state = new MiningPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = "SHIP-MINER",
            ResourceSymbol = "IRON_ORE",
            TargetWaypointSymbol = "X1-AST-1",
            Status = MiningPlanStatus.Active,
        };

        var repo = new PlanRepository(Db);
        await repo.UpsertAsync("MiningSingleResource", state);

        await using var fresh = CreateFreshContext();
        var readRepo = new PlanRepository(fresh);
        var roundTrip = await readRepo.GetAsync<MiningPlanState>("MiningSingleResource");

        roundTrip.Should().NotBeNull();
        roundTrip!.PlanId.Should().Be(state.PlanId);
        roundTrip.ShipSymbol.Should().Be("SHIP-MINER");
        roundTrip.ResourceSymbol.Should().Be("IRON_ORE");
        roundTrip.TargetWaypointSymbol.Should().Be("X1-AST-1");
        roundTrip.Status.Should().Be(MiningPlanStatus.Active);
    }

    [SkippableFact]
    public async Task DeleteAsync_RemovesOnlySpecifiedPlanType()
    {
        var scout = new ScoutAllMarketplacesPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = "SHIP-SCOUT",
            StartWaypointSymbol = "X1-START",
            RouteWaypointSymbols = ["X1-START"],
            CurrentRouteIndex = 0,
            Status = ScoutPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var mining = new MiningPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = "SHIP-MINER",
            ResourceSymbol = "COPPER_ORE",
            TargetWaypointSymbol = "X1-AST-2",
            Status = MiningPlanStatus.Active,
        };

        var repo = new PlanRepository(Db);
        await repo.UpsertAsync(PlanTypes.ScoutAllMarketplaces, scout);
        await repo.UpsertAsync("MiningSingleResource", mining);

        await repo.DeleteAsync(PlanTypes.ScoutAllMarketplaces);

        await using var fresh = CreateFreshContext();
        var readRepo = new PlanRepository(fresh);
        var scoutAfterDelete = await readRepo.GetAsync<ScoutAllMarketplacesPlanState>(PlanTypes.ScoutAllMarketplaces);
        var miningAfterDelete = await readRepo.GetAsync<MiningPlanState>("MiningSingleResource");

        scoutAfterDelete.Should().BeNull();
        miningAfterDelete.Should().NotBeNull();
        miningAfterDelete!.ResourceSymbol.Should().Be("COPPER_ORE");
    }

    private enum MiningPlanStatus
    {
        None = 0,
        Active = 1,
        Completed = 2,
    }

    private sealed record MiningPlanState
    {
        public required Guid PlanId { get; init; }

        public required string ShipSymbol { get; init; }

        public required string ResourceSymbol { get; init; }

        public required string TargetWaypointSymbol { get; init; }

        public required MiningPlanStatus Status { get; init; }
    }
}
