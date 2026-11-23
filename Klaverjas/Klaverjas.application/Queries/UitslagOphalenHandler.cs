using Klaverjas.core.Tafels;
using System.Globalization;

namespace Klaverjas.application.Queries;

/// <summary>
/// Fills the entry screen. A new evening lists every active member as "Speelt", except the slapers
/// Dad picked when preparing it, who start as "Slaper" in their round (D1, D12); an entered evening
/// lists who is on it, as saved.
/// </summary>
public sealed class UitslagOphalenHandler(IAvondStore avonden, ISpelerStore spelers, IInstellingenStore instellingen, IVoorbereidingStore voorbereidingen)
{
    private readonly IVoorbereidingStore voorbereidingen = voorbereidingen ?? throw new ArgumentNullException(nameof(voorbereidingen));
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

        var voorbereid = await voorbereidingen.LadenAsync(datum, cancellationToken);
        var regels = geladen.IsValid
            ? geladen.Value.Uitslagen.Where(u => perId.ContainsKey(u.SpelerId)).Select(u => Regel(perId[u.SpelerId], u)).ToList()
            : [.. alle.Where(s => s.IsLid && s.DoetMee).Select(s => Nieuw(s, voorbereid))];

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

    /// <summary>A member's row on a new evening: a slaper picked in the preparation starts as Slaper in their round.</summary>
    private static UitslagRegel Nieuw(Speler speler, Result<Voorbereiding> voorbereid)
    {
        if (!voorbereid.IsValid || !Tafelschemas.KanMet(voorbereid.Value.Verwacht))
        {
            return new UitslagRegel { Speler = speler };
        }

        var nummer = voorbereid.Value.SlaperNummer(speler.Id);
        return nummer == 0
            ? new UitslagRegel { Speler = speler }
            : new UitslagRegel { Speler = speler, Status = Status.Slaper, Ronde = Tafelschemas.Voor(voorbereid.Value.Verwacht).RondeStil(nummer) };
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
