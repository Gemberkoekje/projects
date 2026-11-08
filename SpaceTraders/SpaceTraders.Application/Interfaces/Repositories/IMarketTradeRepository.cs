using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

/// <summary>
/// The other agents' transactions the markets have listed (PLAN.md slice 6.47, D133): how much other players trade where, so a
/// predicted price is trusted less where they do.
/// </summary>
public interface IMarketTradeRepository
{
    /// <summary>Stores the transactions a market listed that aren't stored yet; one already stored is left as it is.</summary>
    /// <param name="systemSymbol">The market's system.</param>
    /// <param name="trades">The transactions, other agents' only.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>How many were new.</returns>
    Task<int> RecordAsync(string systemSymbol, IReadOnlyList<MarketTradeModel> trades, CancellationToken cancellationToken = default);

    /// <summary>How many transactions each market listed since a time, and what they came to, by market.</summary>
    /// <param name="since">The start.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>One count per market with any.</returns>
    Task<IReadOnlyList<MarketTradeCount>> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}

/// <summary>How many of the other agents' transactions a market listed (<see cref="IMarketTradeRepository.CountSinceAsync"/>).</summary>
public sealed record MarketTradeCount
{
    /// <summary>Creates a count.</summary>
    /// <param name="SystemSymbol">The market's system.</param>
    /// <param name="WaypointSymbol">The market.</param>
    /// <param name="Trades">The transactions.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MarketTradeCount(string SystemSymbol, string WaypointSymbol, int Trades)
    {
        this.SystemSymbol = SystemSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.Trades = Trades;
    }

    /// <summary>The market's system.</summary>
    public required string SystemSymbol { get; init; }

    /// <summary>The market.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>The transactions.</summary>
    public required int Trades { get; init; }

    /// <summary>What they came to, units times price, in credits (PLAN.md slice 6.48, D135); 0 unless set.</summary>
    public long Credits { get; init; }
}
