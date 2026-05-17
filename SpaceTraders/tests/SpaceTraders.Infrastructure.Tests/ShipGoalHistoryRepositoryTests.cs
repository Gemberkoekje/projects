using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

/// <summary>
/// Phase 17e: integration tests for <see cref="ShipGoalHistoryRepository"/>.
/// Verifies that history entries are persisted and retrieved correctly.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ShipGoalHistoryRepositoryTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task AppendAsync_AndGetRecentAsync_RoundTripsCompletedEntry()
    {
        var entry = new ShipGoalHistoryEntry
        {
            Id = Guid.NewGuid(),
            ShipSymbol = "SHIP-H1",
            GoalKind = "MineResource",
            GoalId = Guid.NewGuid(),
            Outcome = "Completed",
            Reason = null,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            EndedAt = DateTimeOffset.UtcNow,
        };

        var repo = new ShipGoalHistoryRepository(Db);
        await repo.AppendAsync(entry);

        await using var fresh = CreateFreshContext();
        var queryRepo = new ShipGoalHistoryRepository(fresh);
        var results = await queryRepo.GetRecentAsync("SHIP-H1", 10);

        results.Should().HaveCount(1);
        var result = results[0];
        result.Id.Should().Be(entry.Id);
        result.ShipSymbol.Should().Be("SHIP-H1");
        result.GoalKind.Should().Be("MineResource");
        result.GoalId.Should().Be(entry.GoalId);
        result.Outcome.Should().Be("Completed");
        result.Reason.Should().BeNull();
        result.StartedAt.Should().BeCloseTo(entry.StartedAt, TimeSpan.FromMilliseconds(1));
        result.EndedAt.Should().BeCloseTo(entry.EndedAt, TimeSpan.FromMilliseconds(1));
        result.CreditsEarned.Should().Be(0);
        result.CreditsSpent.Should().Be(0);
        result.NetCredits.Should().Be(0);
        result.LedgerEntryCount.Should().Be(0);
        result.DurationSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [SkippableFact]
    public async Task AppendAsync_AndGetRecentAsync_RoundTripsBlockedEntry()
    {
        var entry = new ShipGoalHistoryEntry
        {
            Id = Guid.NewGuid(),
            ShipSymbol = "SHIP-H2",
            GoalKind = "SellCargo",
            GoalId = Guid.NewGuid(),
            Outcome = "Blocked",
            Reason = "No market available",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            EndedAt = DateTimeOffset.UtcNow,
        };

        var repo = new ShipGoalHistoryRepository(Db);
        await repo.AppendAsync(entry);

        await using var fresh = CreateFreshContext();
        var queryRepo = new ShipGoalHistoryRepository(fresh);
        var results = await queryRepo.GetRecentAsync("SHIP-H2", 10);

        results.Should().HaveCount(1);
        results[0].Outcome.Should().Be("Blocked");
        results[0].Reason.Should().Be("No market available");
    }

    [SkippableFact]
    public async Task GetRecentAsync_ReturnsNewestFirst()
    {
        var repo = new ShipGoalHistoryRepository(Db);
        var baseTime = DateTimeOffset.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            await repo.AppendAsync(new ShipGoalHistoryEntry
            {
                Id = Guid.NewGuid(),
                ShipSymbol = "SHIP-H3",
                GoalKind = "Idle",
                GoalId = Guid.NewGuid(),
                Outcome = "Completed",
                Reason = null,
                StartedAt = baseTime.AddMinutes(-10 + i),
                EndedAt = baseTime.AddMinutes(-5 + i),
            });
        }

        await using var fresh = CreateFreshContext();
        var queryRepo = new ShipGoalHistoryRepository(fresh);
        var results = await queryRepo.GetRecentAsync("SHIP-H3", 10);

        results.Should().HaveCount(3);
        results[0].EndedAt.Should().BeAfter(results[1].EndedAt);
        results[1].EndedAt.Should().BeAfter(results[2].EndedAt);
    }

    [SkippableFact]
    public async Task GetRecentAsync_RespectsLimit()
    {
        var repo = new ShipGoalHistoryRepository(Db);
        for (var i = 0; i < 5; i++)
        {
            await repo.AppendAsync(new ShipGoalHistoryEntry
            {
                Id = Guid.NewGuid(),
                ShipSymbol = "SHIP-H4",
                GoalKind = "Idle",
                GoalId = Guid.NewGuid(),
                Outcome = "Completed",
                Reason = null,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-i - 1),
                EndedAt = DateTimeOffset.UtcNow.AddMinutes(-i),
            });
        }

        await using var fresh = CreateFreshContext();
        var queryRepo = new ShipGoalHistoryRepository(fresh);
        var results = await queryRepo.GetRecentAsync("SHIP-H4", 3);

        results.Should().HaveCount(3);
    }

    [SkippableFact]
    public async Task GetRecentAsync_IsIsolatedByShipSymbol()
    {
        var repo = new ShipGoalHistoryRepository(Db);
        await repo.AppendAsync(new ShipGoalHistoryEntry
        {
            Id = Guid.NewGuid(),
            ShipSymbol = "SHIP-H5A",
            GoalKind = "Idle",
            GoalId = Guid.NewGuid(),
            Outcome = "Completed",
            Reason = null,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            EndedAt = DateTimeOffset.UtcNow,
        });
        await repo.AppendAsync(new ShipGoalHistoryEntry
        {
            Id = Guid.NewGuid(),
            ShipSymbol = "SHIP-H5B",
            GoalKind = "Idle",
            GoalId = Guid.NewGuid(),
            Outcome = "Blocked",
            Reason = "Other ship",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            EndedAt = DateTimeOffset.UtcNow,
        });

        await using var fresh = CreateFreshContext();
        var queryRepo = new ShipGoalHistoryRepository(fresh);
        var resultsA = await queryRepo.GetRecentAsync("SHIP-H5A", 10);
        var resultsB = await queryRepo.GetRecentAsync("SHIP-H5B", 10);

        resultsA.Should().HaveCount(1);
        resultsA[0].ShipSymbol.Should().Be("SHIP-H5A");
        resultsB.Should().HaveCount(1);
        resultsB[0].ShipSymbol.Should().Be("SHIP-H5B");
    }

    [SkippableFact]
    public async Task GetShipSummaryAsync_ReturnsEarnedSpentAndNetCreditsForShipWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var ledger = new LedgerRepository(Db, new StubActiveRunIdProvider());

        await ledger.AppendAsync("SHIP-L1", SpaceTraders.Domain.Enums.LedgerCategory.TradeSell, 500, cancellationToken: CancellationToken.None);
        await ledger.AppendAsync("SHIP-L1", SpaceTraders.Domain.Enums.LedgerCategory.FuelPurchase, -120, cancellationToken: CancellationToken.None);
        await ledger.AppendAsync("SHIP-L1", SpaceTraders.Domain.Enums.LedgerCategory.Repair, -30, cancellationToken: CancellationToken.None);
        await ledger.AppendAsync("SHIP-L2", SpaceTraders.Domain.Enums.LedgerCategory.TradeSell, 999, cancellationToken: CancellationToken.None);

        var summary = await ledger.GetShipSummaryAsync("SHIP-L1", now.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        summary.CreditsEarned.Should().Be(500);
        summary.CreditsSpent.Should().Be(150);
        summary.NetCredits.Should().Be(350);
        summary.EntryCount.Should().Be(3);
    }

    private sealed class StubActiveRunIdProvider : SpaceTraders.Application.Interfaces.IActiveRunIdProvider
    {
        public Guid? ActiveRunId => null;

        public void SetActiveRunId(Guid? runId)
        {
        }
    }
}
