namespace Kluskat.application.Queries;

/// <summary>Scores the completion log and pairs it with the stored profile.</summary>
public sealed class ProfielOphalenHandler(IVoltooiingStore voltooiingen, IKlusOverzichtStore overzichten, IProfielStore profielen)
{
    private readonly IVoltooiingStore voltooiingen = voltooiingen ?? throw new ArgumentNullException(nameof(voltooiingen));
    private readonly IKlusOverzichtStore overzichten = overzichten ?? throw new ArgumentNullException(nameof(overzichten));
    private readonly IProfielStore profielen = profielen ?? throw new ArgumentNullException(nameof(profielen));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The score and the look.</returns>
    public Task<Result<Profieloverzicht>> HandleAsync(ProfielOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(cancellationToken);
    }

    private async Task<Result<Profieloverzicht>> OphalenAsync(CancellationToken cancellationToken)
    {
        var gegevens = await Spelgegevens.LadenAsync(voltooiingen, overzichten, profielen, cancellationToken);
        var spelstand = gegevens.Spelstand(Clock.UtcNow());

        return Result.For(new Profieloverzicht { Spelstand = spelstand, Accessoire = gegevens.Draagt(spelstand) });
    }
}
