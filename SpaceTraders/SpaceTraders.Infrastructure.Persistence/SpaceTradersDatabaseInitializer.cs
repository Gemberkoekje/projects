using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace SpaceTraders.Infrastructure.Persistence;

/// <summary>
/// Creates the schema from the model. There are no migrations: a database from before the agent
/// id (slice 1.4) doesn't fit the model and has to be dropped first. The cluster's database starts
/// empty.
/// </summary>
public static class SpaceTradersDatabaseInitializer
{
    public static Task InitializeAsync(
        SpaceTradersDbContext dbContext,
        CancellationToken cancellationToken = default)
        => CreateTablesIfMissingAsync(dbContext, cancellationToken);

    /// <summary>
    /// Creates the database and the model's tables when they don't exist yet. <c>EnsureCreated</c>
    /// isn't enough: it does nothing as soon as the database holds any table, even one the app
    /// doesn't own (B21).
    /// </summary>
    private static async Task CreateTablesIfMissingAsync(SpaceTradersDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        var creator = dbContext.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken))
        {
            await creator.CreateAsync(cancellationToken);
        }

        var modelTables = dbContext.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .OfType<string>()
            .Distinct()
            .ToArray();

        var existingModelTables = await dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = ANY({modelTables})")
            .SingleAsync(cancellationToken);

        if (existingModelTables == 0)
        {
            await creator.CreateTablesAsync(cancellationToken);
        }
    }
}
