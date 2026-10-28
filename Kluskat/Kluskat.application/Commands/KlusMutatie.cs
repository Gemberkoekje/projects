namespace Kluskat.application.Commands;

/// <summary>The load → mutate → save round every single-klus command makes.</summary>
internal static class KlusMutatie
{
    /// <summary>Loads a klus, applies a mutation and saves it.</summary>
    /// <param name="repository">Where klussen live.</param>
    /// <param name="id">The klus.</param>
    /// <param name="mutatie">What to do to it.</param>
    /// <param name="cancellationToken">Cancels the round.</param>
    /// <returns>The saved klus, or the first messages that stopped it.</returns>
    internal static async Task<Result<Klus>> UitvoerenAsync(
        IKlusRepository repository,
        KlusId id,
        Func<Klus, Result<Klus>> mutatie,
        CancellationToken cancellationToken)
    {
        var geladen = await repository.LadenAsync(id, cancellationToken);
        if (!geladen.IsValid)
        {
            return geladen;
        }

        var gemuteerd = mutatie(geladen.Value);
        if (!gemuteerd.IsValid)
        {
            return gemuteerd;
        }

        var opgeslagen = await repository.OpslaanAsync(gemuteerd.Value, cancellationToken);
        return opgeslagen.IsValid ? gemuteerd : Result.WithMessages<Klus>(opgeslagen.Messages);
    }

    /// <summary>The messages of a failed result on one line, for a log entry.</summary>
    /// <param name="result">The failed result.</param>
    /// <returns>The messages, separated by semicolons.</returns>
    internal static string Redenen(this Result result)
        => string.Join("; ", result.Messages.Select(m => m.Message));
}
