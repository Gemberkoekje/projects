using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Stores the other agents' transactions a market listed (PLAN.md slice 6.47, D133): a ship whose symbol isn't ours, our agent's
/// symbol and a dash, belongs to another agent. Asked on 2026-11-08: "The more other players are trading there, the lower the
/// prediction accuracy should be."
/// </summary>
public sealed class MarketTradeHandler(IMarketTradeRepository trades, IAgentRepository agents)
{
    public async Task Handle(MarketTradesListedEvent @event, CancellationToken cancellationToken)
    {
        if (await agents.GetAsync(cancellationToken) is not { } agent)
        {
            return;
        }

        var ours = agent.Symbol + "-";
        var others = @event.Trades
            .Where(trade => !trade.ShipSymbol.StartsWith(ours, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await trades.RecordAsync(@event.SystemSymbol, others, cancellationToken);
    }
}
