using System.Globalization;

namespace Klaverjas.application.Queries;

/// <summary>
/// Fills the entry screen. A new evening lists every active member as "Speelt" (D1); an entered
/// evening lists who is on it, as saved.
/// </summary>
public sealed class UitslagOphalenHandler(IAvondStore avonden, ISpelerStore spelers, IInstellingenStore instellingen)
{
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The evening.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The screen, or an error for an evening without play.</returns>
    public Task<Result<UitslagFormulier>> HandleAsync(UitslagOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(query.Datum, cancellationToken);
    }

    private async Task<Result<UitslagFormulier>> OphalenAsync(DateOnly datum, CancellationToken cancellationToken)
    {
        var instelling = await instellingen.LadenAsync(cancellationToken);
        if (instelling.IsGeenSpeelavond(datum))
        {
            return Result.WithMessages<UitslagFormulier>(ValidationMessage.Error("Op die avond wordt niet geklaverjast (zie Instellingen)."));
        }

        var alle = await spelers.AllesAsync(cancellationToken);
        var perId = alle.ToDictionary(s => s.Id);
        var geladen = await avonden.LadenAsync(datum, cancellationToken);

        var regels = geladen.IsValid
            ? geladen.Value.Uitslagen.Where(u => perId.ContainsKey(u.SpelerId)).Select(u => Regel(perId[u.SpelerId], u)).ToList()
            : [.. alle.Where(s => s.IsLid && s.DoetMee).Select(s => new UitslagRegel { Speler = s })];

        var opHetScherm = regels.Select(r => r.Speler.Id).ToHashSet();

        return Result.For(new UitslagFormulier
        {
            Datum = datum,
            IsNieuw = !geladen.IsValid,
            Leden = Gesorteerd(regels.Where(r => r.Speler.IsLid)),
            Invallers = Gesorteerd(regels.Where(r => !r.Speler.IsLid)),
            AndereInvallers = [.. alle
                .Where(s => s.Soort == Soort.Invaller && s.DoetMee && !opHetScherm.Contains(s.Id))
                .OrderBy(s => s.Naam, StandBerekening.NaamVolgorde)],
            PuntenAfwezig = instelling.PuntenAfwezig,
        });
    }

    private static UitslagRegel Regel(Speler speler, Uitslag uitslag) => new UitslagRegel
    {
        Speler = speler,
        Status = uitslag.Status,
        Punten = uitslag.Status == Status.Afwezig ? string.Empty : uitslag.Punten.ToString(CultureInfo.InvariantCulture),
        Pitten = uitslag.Status == Status.Afwezig ? string.Empty : uitslag.Pitten.ToString(CultureInfo.InvariantCulture),
        Ronde = uitslag.Ronde,
    };

    private static List<UitslagRegel> Gesorteerd(IEnumerable<UitslagRegel> regels)
        => [.. regels.OrderBy(r => r.Speler.Naam, StandBerekening.NaamVolgorde)];
}
