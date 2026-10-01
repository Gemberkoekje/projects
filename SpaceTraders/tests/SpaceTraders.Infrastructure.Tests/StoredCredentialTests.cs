using FluentAssertions;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

/// <summary>
/// Tests that <see cref="StoredCredential"/> round-trips through the DB,
/// covering the <c>AgentBootstrapService</c> insert-on-first-run /
/// read-back-on-second-run pattern described in <c>11-testing.md §11.4</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StoredCredentialTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task Insert_ThenFindByKey_ReturnsStoredValue()
    {
        var now = DateTimeOffset.UtcNow;

        Db.Credentials.Add(new StoredCredential
        {
            AgentId = Db.AgentId,
            Key = "AgentToken",
            Value = "test-token-abc",
            StoredAt = now,
        });
        await Db.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        var credential = await fresh.Credentials.FindAsync(fresh.AgentId, "AgentToken");

        credential.Should().NotBeNull();
        credential!.Value.Should().Be("test-token-abc");
        credential.StoredAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [SkippableFact]
    public async Task InsertTwice_WithSameKey_UpdatesValue()
    {
        Db.Credentials.Add(new StoredCredential
        {
            AgentId = Db.AgentId,
            Key = "AgentToken",
            Value = "first-value",
            StoredAt = DateTimeOffset.UtcNow,
        });
        await Db.SaveChangesAsync();

        // Simulate second run: read, then update
        await using var second = CreateFreshContext();
        var existing = await second.Credentials.FindAsync(second.AgentId, "AgentToken");
        existing.Should().NotBeNull();

        second.Entry(existing!).CurrentValues.SetValues(new StoredCredential
        {
            AgentId = second.AgentId,
            Key = "AgentToken",
            Value = "updated-value",
            StoredAt = existing.StoredAt,
        });
        await second.SaveChangesAsync();

        await using var third = CreateFreshContext();
        var result = await third.Credentials.FindAsync(third.AgentId, "AgentToken");
        result!.Value.Should().Be("updated-value");
    }

    [SkippableFact]
    public async Task FirstRun_NoCredential_Exists_SecondRun_CanReadBack()
    {
        // First run: nothing in DB yet
        var before = await Db.Credentials.FindAsync(Db.AgentId, "AgentToken");
        before.Should().BeNull();

        // Bootstrap inserts the token
        Db.Credentials.Add(new StoredCredential
        {
            AgentId = Db.AgentId,
            Key = "AgentToken",
            Value = "bootstrap-token",
            StoredAt = DateTimeOffset.UtcNow,
        });
        await Db.SaveChangesAsync();

        // Second run: reads back the stored token
        await using var secondRun = CreateFreshContext();
        var stored = await secondRun.Credentials.FindAsync(secondRun.AgentId, "AgentToken");
        stored.Should().NotBeNull();
        stored!.Value.Should().Be("bootstrap-token");
    }
}
