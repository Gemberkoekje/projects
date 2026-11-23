using Klaverjas.core.Tafels;

namespace Klaverjas.application.Queries;

/// <summary>
/// Fills the preparation screen: the stored preparation, or for a new one every active member with a
/// card, two blank cards, and as many players expected as there are cards.
/// </summary>
public sealed class VoorbereidingOphalenHandler(IVoorbereidingStore voorbereidingen, ISpelerStore spelers, IAvondStore avonden)
{
    private readonly IVoorbereidingStore voorbereidingen = voorbereidingen ?? throw new ArgumentNullException(nameof(voorbereidingen));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The evening.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The screen.</returns>
    public Task<Result<VoorbereidingFormulier>> HandleAsync(VoorbereidingOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(query.Datum, cancellationToken);
    }

    private async Task<Result<VoorbereidingFormulier>> OphalenAsync(DateOnly datum, CancellationToken cancellationToken)
    {
        var actief = (await spelers.AllesAsync(cancellationToken))
            .Where(s => s.IsLid && s.DoetMee)
            .OrderBy(s => s.Naam, StandBerekening.NaamVolgorde)
            .ToList();
        var geschiedenis = SlaperKeuze.Tel(await avonden.AllesAsync(cancellationToken), datum.Year);
        var opgeslagen = await voorbereidingen.LadenAsync(datum, cancellationToken);

        var voorbereiding = opgeslagen.IsValid
            ? opgeslagen.Value
            : new Voorbereiding
            {
                Id = Voorbereiding.IdVan(datum),
                Datum = datum,
                Kaartjes = [.. actief.Select(s => s.Id)],
                Verwacht = Tafelschemas.Dichtstbij(actief.Count),
            };

        var schema = Tafelschemas.Voor(Tafelschemas.Dichtstbij(voorbereiding.Verwacht));

        return Result.For(new VoorbereidingFormulier
        {
            Voorbereiding = voorbereiding with { Verwacht = schema.Aantal, Slapers = SlaperKeuze.Passend(voorbereiding.Slapers, schema.Slapers) },
            Leden = [.. actief.Select(s => new Lid { Speler = s, Geslapen = geschiedenis.TryGetValue(s.Id, out var g) ? g : new Slaapgeschiedenis() })],
            Schema = schema,
        });
    }
}
