using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

/// <summary>
/// A market listed its recent transactions when one of our ships looked at it (PLAN.md slice 6.47, D133); every agent's, ours
/// included.
/// </summary>
public sealed record MarketTradesListedEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="SystemSymbol">The market's system.</param>
    /// <param name="Trades">The transactions it listed.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MarketTradesListedEvent(string SystemSymbol, IReadOnlyList<MarketTradeModel> Trades)
    {
        this.SystemSymbol = SystemSymbol;
        this.Trades = Trades;
    }

    /// <summary>The market's system.</summary>
    public required string SystemSymbol { get; init; }

    /// <summary>The transactions it listed.</summary>
    public required IReadOnlyList<MarketTradeModel> Trades { get; init; }
}
