using FluentAssertions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class ScoutPlanRepositoryTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task UpsertAsync_AndGetAsync_RoundTripsPlanState()
    {
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var updatedAt = DateTimeOffset.UtcNow;
        var state = new ScoutAllMarketplacesPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = "SHIP-1",
            StartWaypointSymbol = "X1-START",
            RouteWaypointSymbols = ["X1-START", "X1-MARKET-1", "X1-MARKET-2"],
            CurrentRouteIndex = 1,
            Status = ScoutPlanStatus.Active,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };

        var planRepo = new PlanRepository(Db);
        var repo = new ScoutPlanRepository(planRepo);
        await repo.UpsertAsync(state);

        await using var fresh = CreateFreshContext();
        var readPlanRepo = new PlanRepository(fresh);
        var readRepo = new ScoutPlanRepository(readPlanRepo);
        var roundTrip = await readRepo.GetAsync();

        roundTrip.Should().NotBeNull();
        roundTrip!.PlanId.Should().Be(state.PlanId);
        roundTrip.ShipSymbol.Should().Be("SHIP-1");
        roundTrip.StartWaypointSymbol.Should().Be("X1-START");
        roundTrip.RouteWaypointSymbols.Should().Equal("X1-START", "X1-MARKET-1", "X1-MARKET-2");
        roundTrip.CurrentRouteIndex.Should().Be(1);
        roundTrip.Status.Should().Be(ScoutPlanStatus.Active);
        roundTrip.CreatedAt.Should().BeCloseTo(createdAt, TimeSpan.FromMilliseconds(1));
        roundTrip.UpdatedAt.Should().BeCloseTo(updatedAt, TimeSpan.FromMilliseconds(1));
    }

    [SkippableFact]
    public async Task UpsertAsync_OverwritesExistingPlanState()
    {
        var planRepo = new PlanRepository(Db);
        var repo = new ScoutPlanRepository(planRepo);

        await repo.UpsertAsync(new ScoutAllMarketplacesPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = "SHIP-1",
            StartWaypointSymbol = "X1-START",
            RouteWaypointSymbols = ["X1-START"],
            CurrentRouteIndex = 0,
            Status = ScoutPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });

        var replacement = new ScoutAllMarketplacesPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = "SHIP-2",
            StartWaypointSymbol = "X1-ALT",
            RouteWaypointSymbols = ["X1-ALT", "X1-ALT-MARKET"],
            CurrentRouteIndex = 1,
            Status = ScoutPlanStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await repo.UpsertAsync(replacement);

        await using var fresh = CreateFreshContext();
        var readPlanRepo = new PlanRepository(fresh);
        var readRepo = new ScoutPlanRepository(readPlanRepo);
        var stored = await readRepo.GetAsync();

        stored.Should().NotBeNull();
        stored!.PlanId.Should().Be(replacement.PlanId);
        stored.ShipSymbol.Should().Be("SHIP-2");
        stored.StartWaypointSymbol.Should().Be("X1-ALT");
        stored.RouteWaypointSymbols.Should().Equal("X1-ALT", "X1-ALT-MARKET");
        stored.CurrentRouteIndex.Should().Be(1);
        stored.Status.Should().Be(ScoutPlanStatus.Completed);
    }

    [SkippableFact]
    public async Task DeleteAsync_RemovesPlanState()
    {
        var planRepo = new PlanRepository(Db);
        var repo = new ScoutPlanRepository(planRepo);

        await repo.UpsertAsync(new ScoutAllMarketplacesPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = "SHIP-1",
            StartWaypointSymbol = "X1-START",
            RouteWaypointSymbols = ["X1-START", "X1-MARKET"],
            CurrentRouteIndex = 0,
            Status = ScoutPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await repo.DeleteAsync();

        await using var fresh = CreateFreshContext();
        var readPlanRepo = new PlanRepository(fresh);
        var readRepo = new ScoutPlanRepository(readPlanRepo);
        var stored = await readRepo.GetAsync();

        stored.Should().BeNull();
    }
}
