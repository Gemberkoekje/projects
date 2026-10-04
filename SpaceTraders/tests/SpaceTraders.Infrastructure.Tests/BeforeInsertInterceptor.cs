using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SpaceTraders.Infrastructure.Tests;

/// <summary>
/// Runs another writer once, just before the context's first insert into a table goes to the database. The other writer's
/// row then appears between this writer's read and its write, as when two scopes store a row nobody stored before at the
/// same moment (B61).
/// </summary>
/// <param name="table">The table whose first insert waits for the other writer.</param>
/// <param name="otherWriter">The other writer, on a context of its own.</param>
[Mutable]
public sealed class BeforeInsertInterceptor(string table, Func<Task> otherWriter) : DbCommandInterceptor
{
    private readonly string _insert = $"INSERT INTO {table} ";

    /// <summary>Whether the other writer has run.</summary>
    public bool HasRun { get; private set; }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await RunOtherWriterBeforeInsertAsync(command);
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await RunOtherWriterBeforeInsertAsync(command);
        return result;
    }

    private async Task RunOtherWriterBeforeInsertAsync(DbCommand command)
    {
        if (HasRun || !command.CommandText.Contains(_insert, StringComparison.Ordinal))
        {
            return;
        }

        HasRun = true;
        await otherWriter();
    }
}
