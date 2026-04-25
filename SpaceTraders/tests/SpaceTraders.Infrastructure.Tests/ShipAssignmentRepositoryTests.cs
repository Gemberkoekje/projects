using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class ShipAssignmentRepositoryTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task UpsertAsync_NewAssignment_CanBeResumedByStepIndex()
    {
        var repo = new ShipAssignmentRepository(Db);
        var now = DateTimeOffset.UtcNow;
        var assignment = new ShipAssignmentDto(
            ShipSymbol: "SHIP-A",
            AssignmentType: "Trade",
            OriginWaypoint: "WP1",
            DestWaypoint: "WP2",
            CargoSymbol: "IRON_ORE",
            ContractId: null,
            StepIndex: 2,
            AssignedAt: now,
            CompletedAt: null);

        await repo.UpsertAsync(assignment);

        await using var fresh = CreateFreshContext();
        var result = await new ShipAssignmentRepository(fresh).FindAsync("SHIP-A");

        result.Should().NotBeNull();
        result!.StepIndex.Should().Be(2, "StepIndex must be persisted so the ship can resume mid-route");
        result.AssignmentType.Should().Be("Trade");
        result.OriginWaypoint.Should().Be("WP1");
        result.DestWaypoint.Should().Be("WP2");
        result.CargoSymbol.Should().Be("IRON_ORE");
    }

    [SkippableFact]
    public async Task UpsertAsync_AdvancingStepIndex_UpdatesPersisted()
    {
        var repo = new ShipAssignmentRepository(Db);
        var now = DateTimeOffset.UtcNow;
        await repo.UpsertAsync(new ShipAssignmentDto("SHIP-B", "Trade", "WP1", "WP2", null, null, 0, now, null));

        // Advance through the route
        await repo.UpsertAsync(new ShipAssignmentDto("SHIP-B", "Trade", "WP1", "WP2", null, null, 1, now, null));
        await repo.UpsertAsync(new ShipAssignmentDto("SHIP-B", "Trade", "WP1", "WP2", null, null, 2, now, null));

        await using var fresh = CreateFreshContext();
        var result = await new ShipAssignmentRepository(fresh).FindAsync("SHIP-B");

        result!.StepIndex.Should().Be(2);
    }

    [SkippableFact]
    public async Task FindAsync_NonExistentShip_ReturnsNull()
    {
        var repo = new ShipAssignmentRepository(Db);
        var result = await repo.FindAsync("GHOST-SHIP");
        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task UpsertAsync_WithCompletedAt_PersistsTimestamp()
    {
        var repo = new ShipAssignmentRepository(Db);
        var assigned = DateTimeOffset.UtcNow.AddMinutes(-30);
        var completed = DateTimeOffset.UtcNow;

        await repo.UpsertAsync(new ShipAssignmentDto(
            "SHIP-C", "Trade", "WP1", "WP2", "GOLD", null, 3, assigned, completed));

        await using var fresh = CreateFreshContext();
        var result = await new ShipAssignmentRepository(fresh).FindAsync("SHIP-C");

        result!.CompletedAt.Should().NotBeNull();
        result.CompletedAt!.Value.Should().BeCloseTo(completed, TimeSpan.FromSeconds(1));
    }
}
