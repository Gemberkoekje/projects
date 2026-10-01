using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence;

/// <summary>
/// Keeps the rows of one agent only: the active one. An earlier agent's rows (the agent before a
/// server reset, say) are deleted, except its run summaries in <c>runs</c>.
/// </summary>
public static class AgentDataCleanup
{
    // Deleting a whole agent can take longer than the usual 30 s command timeout. It only happens
    // once per agent; after that each statement finds nothing to delete.
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    /// <summary>The tables whose rows belong to an agent and are deleted with it.</summary>
    public static IReadOnlyList<string> AgentTables(IModel model) =>
        model.GetEntityTypes()
            .Where(entity => entity.ClrType != typeof(Run) && entity.FindProperty(nameof(Run.AgentId)) is not null)
            .Select(entity => entity.GetTableName())
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Deletes every other agent's rows, table by table. A table that fails is logged and left
    /// for the next start. Needs a relational database; the in-memory one of unit tests is left
    /// alone.
    /// </summary>
    public static async Task DeleteOtherAgentsAsync(SpaceTradersDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(db.AgentId);
        if (!db.Database.IsRelational())
        {
            return;
        }

        var sql = db.GetService<ISqlGenerationHelper>();
        db.Database.SetCommandTimeout(CommandTimeout);
        foreach (var table in AgentTables(db.Model))
        {
            try
            {
                // The table and column names come from the model; the agent id is a parameter.
#pragma warning disable EF1002
                var deleted = await db.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM {sql.DelimitIdentifier(table)} WHERE {sql.DelimitIdentifier(nameof(Run.AgentId))} <> {{0}};",
                    [db.AgentId],
                    cancellationToken);
#pragma warning restore EF1002
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Rows} row(s) of earlier agents from {Table}.", deleted, table);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Deleting earlier agents' rows from {Table} failed; the next start tries again.", table);
            }
        }
    }
}
