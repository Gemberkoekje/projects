namespace Klaverjas.application.Queries;

/// <summary>Lists the players for the Spelers screen.</summary>
public sealed class SpelersOphalenHandler(ISpelerStore spelers)
{
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The members by name, then the substitutes by name.</returns>
    public Task<Result<IReadOnlyList<Speler>>> HandleAsync(SpelersOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(cancellationToken);
    }

    private async Task<Result<IReadOnlyList<Speler>>> OphalenAsync(CancellationToken cancellationToken)
    {
        var alle = await spelers.AllesAsync(cancellationToken);
        IReadOnlyList<Speler> gesorteerd = [.. alle.OrderBy(s => s.IsLid ? 0 : 1).ThenBy(s => s.Naam, StandBerekening.NaamVolgorde)];
        return Result.For(gesorteerd);
    }
}
