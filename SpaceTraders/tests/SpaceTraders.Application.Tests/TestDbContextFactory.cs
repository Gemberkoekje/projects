using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;

namespace SpaceTraders.Application.Tests;

internal static class TestDbContextFactory
{
    public static SpaceTradersDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<SpaceTradersDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        return new SpaceTradersDbContext(options);
    }
}
