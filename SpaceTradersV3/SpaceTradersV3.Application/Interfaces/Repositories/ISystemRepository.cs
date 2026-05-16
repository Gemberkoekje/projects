namespace SpaceTraders.Application.Interfaces.Repositories;

public interface ISystemRepository
{
    Task<SystemCacheModel?> FindAsync(string systemSymbol, CancellationToken cancellationToken = default);

    /// <summary>Returns all cached systems ordered by symbol.</summary>
    Task<IReadOnlyList<SystemCacheModel>> GetAllAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(SystemCacheModel system, CancellationToken cancellationToken = default);
}

public sealed record SystemCacheModel
{
    public required string Symbol { get; init; }

    public required string SectorSymbol { get; init; }

    public required string Type { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SystemCacheModel(
        string Symbol,
        string SectorSymbol,
        string Type,
        int X,
        int Y,
        DateTimeOffset LastObservedAt)
    {
        this.Symbol = Symbol;
        this.SectorSymbol = SectorSymbol;
        this.Type = Type;
        this.X = X;
        this.Y = Y;
        this.LastObservedAt = LastObservedAt;
    }
}
