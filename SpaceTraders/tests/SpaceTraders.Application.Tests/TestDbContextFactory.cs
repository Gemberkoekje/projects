using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.Application.Tests;

internal static class TestDbContextFactory
{
    public const string AgentId = "APPLICATION-TEST@2026-09-27";

    public static SpaceTradersDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<SpaceTradersDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        var scope = new AgentDataScope();
        scope.Set(AgentId);

        return new SpaceTradersDbContext(options, scope);
    }
}
