using FluentAssertions;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class FleetGoalRepositoryTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task UpsertAsync_AndGetActiveAsync_RoundTripsContractGoal()
    {
        var goal = new FleetGoal(
            Kind: FleetGoalKind.Contract,
            Description: "Deliver 50 IRON_ORE to X1-TEST-D47 for contract c1.",
            Priority: FleetGoalPriority.Contract,
            ContractId: "c1",
            TradeSymbol: "IRON_ORE",
            RemainingUnits: 50);

        var repo = new FleetGoalRepository(Db);
        await repo.UpsertAsync(goal);

        await using var fresh = CreateFreshContext();
        var readRepo = new FleetGoalRepository(fresh);
        var active = await readRepo.GetActiveAsync();

        active.Should().HaveCount(1);
        var result = active[0];
        result.Id.Should().Be(goal.Id);
        result.Kind.Should().Be(FleetGoalKind.Contract);
        result.Description.Should().Be(goal.Description);
        result.Priority.Should().Be(FleetGoalPriority.Contract);
        result.ContractId.Should().Be("c1");
        result.TradeSymbol.Should().Be("IRON_ORE");
        result.RemainingUnits.Should().Be(50);
    }

    [SkippableFact]
    public async Task UpsertAsync_UpdatesExistingGoal_WhenSameIdUsed()
    {
        var goal = new FleetGoal(
            Kind: FleetGoalKind.MarketCoverage,
            Description: "Place probe at X1-TEST-MKT.",
            Priority: FleetGoalPriority.MarketCoverage,
            OriginWaypoint: "X1-TEST-MKT");

        var repo = new FleetGoalRepository(Db);
        await repo.UpsertAsync(goal);

        // Upsert again with the same ID (e.g. priority changed)
        var updated = goal with { Priority = 99, Description = "Updated description." };
        await repo.UpsertAsync(updated);

        await using var fresh = CreateFreshContext();
        var readRepo = new FleetGoalRepository(fresh);
        var active = await readRepo.GetActiveAsync();

        active.Should().HaveCount(1);
        active[0].Priority.Should().Be(99);
        active[0].Description.Should().Be("Updated description.");
    }

    [SkippableFact]
    public async Task MarkCompletedAsync_RemovesGoalFromActiveSet()
    {
        var goal1 = new FleetGoal(
            Kind: FleetGoalKind.Contract,
            Description: "Goal 1",
            Priority: 20,
            ContractId: "c1",
            TradeSymbol: "IRON_ORE");

        var goal2 = new FleetGoal(
            Kind: FleetGoalKind.MarketCoverage,
            Description: "Goal 2",
            Priority: 40,
            OriginWaypoint: "X1-MKT");

        var repo = new FleetGoalRepository(Db);
        await repo.UpsertAsync(goal1);
        await repo.UpsertAsync(goal2);

        await repo.MarkCompletedAsync(goal1.Id);

        await using var fresh = CreateFreshContext();
        var readRepo = new FleetGoalRepository(fresh);
        var active = await readRepo.GetActiveAsync();

        active.Should().HaveCount(1);
        active[0].Id.Should().Be(goal2.Id);
    }

    [SkippableFact]
    public async Task MarkCompletedAsync_DoesNothing_WhenGoalIdNotFound()
    {
        var goal = new FleetGoal(
            Kind: FleetGoalKind.MarketCoverage,
            Description: "Goal",
            Priority: 40,
            OriginWaypoint: "X1-MKT");

        var repo = new FleetGoalRepository(Db);
        await repo.UpsertAsync(goal);

        // Mark a random non-existent ID as completed
        await repo.MarkCompletedAsync(Guid.NewGuid());

        await using var fresh = CreateFreshContext();
        var readRepo = new FleetGoalRepository(fresh);
        var active = await readRepo.GetActiveAsync();

        active.Should().HaveCount(1);
    }

    [SkippableFact]
    public async Task GetActiveAsync_ReturnsEmpty_WhenAllGoalsCompleted()
    {
        var goal = new FleetGoal(
            Kind: FleetGoalKind.Construction,
            Description: "Supply ALUMINUM_ORE to X1-TEST-CS.",
            Priority: FleetGoalPriority.Construction,
            DestinationWaypoint: "X1-TEST-CS",
            TradeSymbol: "ALUMINUM_ORE");

        var repo = new FleetGoalRepository(Db);
        await repo.UpsertAsync(goal);
        await repo.MarkCompletedAsync(goal.Id);

        await using var fresh = CreateFreshContext();
        var readRepo = new FleetGoalRepository(fresh);
        var active = await readRepo.GetActiveAsync();

        active.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task UpsertAsync_RoundTripsFleetExpansionGoal()
    {
        var goal = new FleetGoal(
            Kind: FleetGoalKind.FleetExpansion,
            Description: "Expand fleet: purchasing SHIP_MINING_DRONE at X1-TEST-SY.",
            Priority: FleetGoalPriority.FleetExpansion,
            EstimatedCost: 250_000,
            ExpansionShipType: "SHIP_MINING_DRONE",
            ExpansionShipyardWaypoint: "X1-TEST-SY");

        var repo = new FleetGoalRepository(Db);
        await repo.UpsertAsync(goal);

        await using var fresh = CreateFreshContext();
        var readRepo = new FleetGoalRepository(fresh);
        var active = await readRepo.GetActiveAsync();

        active.Should().HaveCount(1);
        var result = active[0];
        result.Kind.Should().Be(FleetGoalKind.FleetExpansion);
        result.ExpansionShipType.Should().Be("SHIP_MINING_DRONE");
        result.ExpansionShipyardWaypoint.Should().Be("X1-TEST-SY");
        result.EstimatedCost.Should().Be(250_000);
    }
}
