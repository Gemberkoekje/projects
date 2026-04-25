using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.Infrastructure.Persistence;

public static class SpaceTradersDatabaseInitializer
{
    public static async Task InitializeAsync(
        SpaceTradersDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"MountsJson\" text NULL;",
                cancellationToken);
        }

        await DefaultSettingsSeed.SeedAsync(dbContext);
    }
}
