using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.Application.Tests;

internal static class TestDbContextFactory
{
    public const string AgentId = "APPLICATION-TEST@2026-09-27";

    /// <summary>A context for <paramref name="agentId"/>; another agent on the same database, as after a server reset, with the same <paramref name="dbName"/>.</summary>
    public static SpaceTradersDbContext Create(string? dbName = null, string agentId = AgentId)
    {
        var options = new DbContextOptionsBuilder<SpaceTradersDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        var scope = new AgentDataScope();
        scope.Set(agentId);

        return new SpaceTradersDbContext(options, scope);
    }
}
