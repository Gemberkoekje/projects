using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.API.Tests.Services;

public sealed class AgentDataCleanupTests
{
    [Fact]
    public void AgentTables_AreEveryTableButTheRunSummariesTheScheduledShipEventsAndTheNextRunSettings()
    {
        // A new table is deleted with its agent as soon as it has an AgentId. A table without one
        // fails this test, so that someone decides. Scheduled ship events have no agent: they
        // delete themselves when they fire. The values chosen for the next runs have none either:
        // the next agent starts with them (D69).
        var scope = new AgentDataScope();
        scope.Set("AGENT@2026-09-27");
        var options = new DbContextOptionsBuilder<SpaceTradersDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        using var db = new SpaceTradersDbContext(options, scope);
        var allTables = db.Model.GetEntityTypes().Select(entity => entity.GetTableName()!);

        AgentDataCleanup.AgentTables(db.Model).Should().BeEquivalentTo(allTables.Except(["runs", "scheduled_ship_events", "next_run_settings"]));
    }
}
