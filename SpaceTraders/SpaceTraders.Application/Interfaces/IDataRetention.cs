namespace SpaceTraders.Application.Interfaces;

/// <summary>Deletes the rows that tables no longer need, one table at a time.</summary>
public interface IDataRetention
{
    /// <summary>The tables that have rows to prune.</summary>
    IReadOnlyList<string> PrunedTables { get; }

    /// <summary>Deletes the rows of <paramref name="table"/> that its policy no longer keeps, and returns how many.</summary>
    Task<int> PruneAsync(string table, DateTimeOffset now, CancellationToken cancellationToken = default);
}
