using System.Globalization;

namespace Klaverjas.core.Standen;

/// <summary>
/// The rules of Dad's standings (R1–R7, R16–R18) as one pure function of the players, the evenings
/// and the settings. Change a rule here and every week re-scores itself; nothing is stored.
/// </summary>
public static class StandBerekening
{
    /// <summary>Dutch name order, for ties and lists.</summary>
    public static StringComparer NaamVolgorde { get; } = StringComparer.Create(CultureInfo.GetCultureInfo("nl-NL"), ignoreCase: true);

    /// <summary>Computes a season's standings after its latest entered evening.</summary>
    /// <param name="spelers">Every player, members and substitutes.</param>
    /// <param name="avonden">Every entered evening (other seasons are ignored).</param>
    /// <param name="instellingen">The absence score.</param>
    /// <param name="jaar">The season.</param>
    /// <returns>The standings; <see cref="Stand.IsLeeg"/> when the season has no evening yet.</returns>
    public static Stand Bereken(IEnumerable<Speler> spelers, IEnumerable<Avond> avonden, Instellingen instellingen, int jaar)
    {
        ArgumentNullException.ThrowIfNull(spelers);
        ArgumentNullException.ThrowIfNull(avonden);
        ArgumentNullException.ThrowIfNull(instellingen);

        var seizoen = avonden.Where(a => a.Datum.Year == jaar).OrderBy(a => a.Datum).ToList();

        if (seizoen.Count == 0)
        {
            return Stand.Leeg(jaar);
        }

        var alle = spelers.ToList();
        var uitslagen = seizoen.ToDictionary(a => a.Datum, a => a.Uitslagen.ToDictionary(u => u.SpelerId));
        var plaatsen = seizoen.ToDictionary(a => a.Datum, AvondPlaatsen);

        StandRegel Regel(Speler speler)
        {
            var cellen = new Dictionary<DateOnly, Cel>();

            foreach (var datum in seizoen.Select(a => a.Datum))
            {
                var cel = CelVan(speler, datum, uitslagen[datum], plaatsen[datum], instellingen.PuntenAfwezig);
                if (!cel.IsLeeg)
                {
                    cellen[datum] = cel;
                }
            }

            return new StandRegel
            {
                Speler = speler,
                Totaal = cellen.Values.Sum(c => c.Punten),
                Pitten = cellen.Values.Sum(c => c.Pitten),
                Avonden = cellen,
                Laagste = Laagste(cellen),
            };
        }

        var leden = alle.Where(s => s.IsLid && s.InStand).Select(Regel).ToList();
        leden = [.. leden.Select(r => r with
        {
            Plaats = 1 + leden.Count(o => o.Totaal > r.Totaal),
            PlaatsPitten = 1 + leden.Count(o => o.Pitten > r.Pitten),
        })];

        var opPunten = MetVerschil([.. leden.OrderBy(r => r.Plaats).ThenBy(r => r.Speler.Naam, NaamVolgorde)]);
        var opPitten = leden.OrderBy(r => r.PlaatsPitten).ThenBy(r => r.Speler.Naam, NaamVolgorde).ToList();

        var invallers = alle
            .Where(s => s.Soort == Soort.Invaller)
            .Select(Regel)
            .Where(r => r.Avonden.Values.Any(c => !c.IsAfwezig))
            .OrderBy(r => r.Speler.Naam, NaamVolgorde)
            .ToList();

        var laatste = seizoen[^1];

        return new Stand
        {
            Jaar = jaar,
            LaatsteAvond = laatste.Datum,
            Avonden = [.. seizoen.Select(a => a.Datum)],
            Punten = opPunten,
            Pitten = opPitten,
            Invallers = invallers,
            Winnaars = Winnaars(laatste, alle, plaatsen[laatste.Datum]),
            WeekTotalenPitten = seizoen.ToDictionary(a => a.Datum, a => leden.Sum(r => r.Op(a.Datum).Pitten)),
        };
    }

    /// <summary>The place a score takes on an evening: everyone who played counts, absentees don't (R4).</summary>
    /// <param name="avond">The evening.</param>
    /// <returns>A function from points to place; ties share a place.</returns>
    internal static Func<int, int> AvondPlaatsen(Avond avond)
    {
        var scores = avond.Uitslagen.Where(Gespeeld).Select(u => u.Punten).ToList();
        return punten => 1 + scores.Count(s => s > punten);
    }

    private static bool Gespeeld(Uitslag uitslag) => uitslag.Status is Status.Speelt or Status.Slaper;

    private static Cel CelVan(
        Speler speler,
        DateOnly datum,
        Dictionary<Guid, Uitslag> uitslagen,
        Func<int, int> plaats,
        int puntenAfwezig)
    {
        if (uitslagen.TryGetValue(speler.Id, out var uitslag))
        {
            if (uitslag.Status == Status.Afwezig)
            {
                return new Cel { Status = Status.Afwezig, Punten = puntenAfwezig };
            }

            var p = plaats(uitslag.Punten);
            return new Cel { Status = uitslag.Status, Punten = uitslag.Punten, Pitten = uitslag.Pitten, AvondPlaats = p <= 3 ? p : 0 };
        }

        // A member who joined later counts as absent on every evening before (R17).
        return speler.IsLid && datum < speler.LidVanaf
            ? new Cel { Status = Status.Afwezig, Punten = puntenAfwezig }
            : Cel.Leeg;
    }

    private static List<LaagsteAvond> Laagste(Dictionary<DateOnly, Cel> cellen)
        => [.. cellen
            .GroupBy(c => Kalender.Kwartaal(c.Key))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var laagste = g.OrderBy(c => c.Value.Punten).ThenBy(c => c.Key).First();
                return new LaagsteAvond { Kwartaal = g.Key, Datum = laagste.Key, Punten = laagste.Value.Punten };
            })];

    private static List<StandRegel> MetVerschil(List<StandRegel> gesorteerd)
        => [.. gesorteerd.Select((r, i) => r with { Verschil = i == 0 ? 0 : gesorteerd[i - 1].Totaal - r.Totaal })];

    private static List<Winnaar> Winnaars(Avond avond, List<Speler> spelers, Func<int, int> plaats)
    {
        var perId = spelers.ToDictionary(s => s.Id);

        return [.. avond.Uitslagen
            .Where(u => Gespeeld(u) && plaats(u.Punten) <= 3 && perId.ContainsKey(u.SpelerId))
            .Select(u => new Winnaar { Speler = perId[u.SpelerId], Plaats = plaats(u.Punten), Punten = u.Punten })
            .OrderBy(w => w.Plaats)
            .ThenBy(w => w.Speler.Naam, NaamVolgorde)];
    }
}
