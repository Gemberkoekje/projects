namespace Kluskat.application.Queries;

/// <summary>Picks the klus to show. Looking is not a tap: it never extends the skip window.</summary>
public sealed class VolgendeKlusOphalenHandler(IKlusOverzichtStore overzichten, ISessieStore sessies)
{
    private readonly IKlusOverzichtStore overzichten = overzichten ?? throw new ArgumentNullException(nameof(overzichten));
    private readonly ISessieStore sessies = sessies ?? throw new ArgumentNullException(nameof(sessies));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What to show.</returns>
    public Task<Result<Keuze>> HandleAsync(VolgendeKlusOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(query, cancellationToken);
    }

    private async Task<Result<Keuze>> OphalenAsync(VolgendeKlusOphalen query, CancellationToken cancellationToken)
    {
        var nu = Clock.UtcNow();
        var klussen = await overzichten.ActieveAsync(cancellationToken);
        var sessie = (await sessies.LadenAsync(cancellationToken)).Actueel(nu);

        return Result.For(KlusKiezer.Kies(klussen, sessie, nu, query.Vooruit));
    }
}
