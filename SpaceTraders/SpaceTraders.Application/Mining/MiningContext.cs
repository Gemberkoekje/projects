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
}

/// <summary>Reads the <see cref="MiningContext"/> for a system from the cache.</summary>
public interface IMiningContextReader
{
    /// <summary>Reads what a survey or mining decision in a system needs.</summary>
    /// <param name="systemSymbol">The system the ship is in.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The system's map, its usable surveys and the credits.</returns>
    Task<MiningContext> ReadAsync(string systemSymbol, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class MiningContextReader(ITradeContextReader tradeContexts, ISurveyRepository surveys) : IMiningContextReader
{
    /// <inheritdoc />
    public async Task<MiningContext> ReadAsync(string systemSymbol, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var trade = await tradeContexts.ReadAsync(systemSymbol, cancellationToken);
        var usable = (await surveys.GetActiveAsync(cancellationToken))
            .Select(stored => stored.Survey)
            .Where(survey => SurveySelection.IsUsable(survey, now)
                && survey.WaypointSymbol.StartsWith(systemSymbol + "-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return new MiningContext(trade.Map, usable, trade.Credits, now);
    }
}
