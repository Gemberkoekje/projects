using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Mining;

/// <summary>Everything a survey or mining decision reads about one system (PLAN.md slice 6.4).</summary>
public sealed record MiningContext
{
    /// <summary>Creates the context of one decision.</summary>
    /// <param name="Map">The system: waypoints with their traits, markets with their last prices, fuel.</param>
    /// <param name="Surveys">The usable surveys in the cache: not expired, and not found exhausted.</param>
    /// <param name="Credits">The credits on hand, as cached.</param>
    /// <param name="Now">The time the decision is made at.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MiningContext(TradeMarketMap Map, IReadOnlyList<SurveyModel> Surveys, long Credits, DateTimeOffset Now)
    {
        this.Map = Map;
        this.Surveys = Surveys;
        this.Credits = Credits;
        this.Now = Now;
    }

    /// <summary>The system: waypoints with their traits, markets with their last prices, fuel.</summary>
    public required TradeMarketMap Map { get; init; }

    /// <summary>The usable surveys in the cache: not expired, and not found exhausted.</summary>
    public required IReadOnlyList<SurveyModel> Surveys { get; init; }

    /// <summary>The credits on hand, as cached.</summary>
    public required long Credits { get; init; }

    /// <summary>The time the decision is made at.</summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>
    /// The pairs of market and ore (<see cref="MiningPlanner.OpportunityKey"/>) left to the trade (PLAN.md slice 6.40, D122):
    /// abroad, where a trade trip is on its way to sell the ore at the market, no miner mines for it there. Empty unless set;
    /// at home miners and traders share the markets, as before.
    /// </summary>
    public IReadOnlySet<string> LeftToTrade { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Reads the <see cref="MiningContext"/> for a system from the cache.</summary>
public interface IMiningContextReader
{
    /// <summary>Reads what a survey or mining decision in a system needs.</summary>
    /// <param name="systemSymbol">The system the ship is in.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The system's map, its usable surveys and the credits.</returns>
    Task<MiningContext> ReadAsync(string systemSymbol, CancellationToken cancellationToken);

    /// <summary>
    /// The systems abroad whose markets the probes keep fresh (PLAN.md slice 6.40, D122): every system outside those the plans do
    /// business in (<see cref="BusinessSystems"/>) with a market whose prices are recent enough to decide by (D96). None while
    /// home isn't known.
    /// </summary>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The systems, by symbol.</returns>
    Task<IReadOnlyList<string>> SystemsAbroadAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
/// <remarks>
/// Abroad (PLAN.md slice 6.40, D122) a market whose prices are older than <c>Trade.MaxPriceAgeMinutes</c> is stale
/// (<see cref="TradeMarketMap.StaleMarkets"/>): no miner mines for it, as no trade route buys or sells there (D96). At home every
/// market counts, as before: the probes watch every one (D29).
/// </remarks>
public sealed class MiningContextReader(ITradeContextReader tradeContexts, ISurveyRepository surveys, IAgentRepository agents) : IMiningContextReader
{
    /// <inheritdoc />
    public async Task<MiningContext> ReadAsync(string systemSymbol, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var trade = await tradeContexts.ReadAsync(systemSymbol, cancellationToken);
        var map = trade.Map;
        if (BusinessSystems.IsAbroad(BusinessSystems.Of(await agents.GetAsync(cancellationToken)), systemSymbol))
        {
            var fresh = await tradeContexts.FreshMarketsAsync(cancellationToken);
            map = map.WithStaleMarkets(map.MarketWaypoints.Where(market => !fresh.Contains(market)).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }

        var usable = (await surveys.GetActiveAsync(cancellationToken))
            .Select(stored => stored.Survey)
            .Where(survey => SurveySelection.IsUsable(survey, now)
                && survey.WaypointSymbol.StartsWith(systemSymbol + "-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return new MiningContext(map, usable, trade.Credits, now);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SystemsAbroadAsync(CancellationToken cancellationToken)
    {
        var home = BusinessSystems.Of(await agents.GetAsync(cancellationToken));
        if (home.Count == 0)
        {
            return [];
        }

        return [.. (await tradeContexts.FreshMarketsAsync(cancellationToken))
            .Select(WaypointSymbols.SystemOf)
            .Where(system => BusinessSystems.IsAbroad(home, system))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];
    }
}
