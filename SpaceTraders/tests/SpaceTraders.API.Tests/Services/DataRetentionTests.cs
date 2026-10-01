using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.API.Tests.Services;

public sealed class DataRetentionTests
{
    [Fact]
    public void EveryTable_HasARetentionPolicy()
    {
        // B3: nothing pruned startup_snapshots or runs, and nothing made a new table say how long it
        // keeps its rows.
        using var db = CreateContext();
        var tables = db.Model.GetEntityTypes().Select(entity => entity.GetTableName()!).ToList();

        tables.Except(DataRetention.Policies.Keys).Should().BeEquivalentTo(Array.Empty<string>(), "every table needs a retention policy, or the reason it can't grow without bound");
        DataRetention.Policies.Keys.Except(tables).Should().BeEquivalentTo(Array.Empty<string>(), "every policy belongs to a table");
    }

    private static SpaceTradersDbContext CreateContext()
    {
        var scope = new AgentDataScope();
        scope.Set("AGENT@2026-09-27");
        var options = new DbContextOptionsBuilder<SpaceTradersDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new SpaceTradersDbContext(options, scope);
    }
}
