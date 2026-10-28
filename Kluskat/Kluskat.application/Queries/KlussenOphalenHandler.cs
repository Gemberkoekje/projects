namespace Kluskat.application.Queries;

/// <summary>Lists every active klus by room, then name. Only the beheer page ever sees the whole list.</summary>
public sealed class KlussenOphalenHandler(IKlusOverzichtStore overzichten)
{
    private readonly IKlusOverzichtStore overzichten = overzichten ?? throw new ArgumentNullException(nameof(overzichten));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The klussen.</returns>
    public Task<Result<IReadOnlyList<KlusOverzicht>>> HandleAsync(KlussenOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(cancellationToken);
    }

    private async Task<Result<IReadOnlyList<KlusOverzicht>>> OphalenAsync(CancellationToken cancellationToken)
    {
        var klussen = await overzichten.ActieveAsync(cancellationToken);

        IReadOnlyList<KlusOverzicht> gesorteerd =
        [
            .. klussen
                .OrderBy(k => k.Ruimte, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(k => k.Naam, StringComparer.CurrentCultureIgnoreCase),
        ];

        return Result.For(gesorteerd);
    }
}
