namespace Klaverjas.application.Import;

/// <summary>
/// Turns a read workbook into players, evenings and settings (D4), and checks the result against
/// the workbook's own totals with the same rules as the live standings. Pure: nothing is stored here.
/// </summary>
public static class ImportPlanner
{
    /// <summary>The name of the player the unnamed "invallers" row becomes; Dad can rename it.</summary>
    public const string InvallerNaam = "Invaller (uit Excel)";

    /// <summary>Plans a workbook.</summary>
    /// <param name="bestand">The workbook as read.</param>
    /// <param name="spelers">The players already in the app.</param>
    /// <param name="avonden">The evenings already in the app.</param>
    /// <param name="instellingen">The settings (the absence score).</param>
    /// <param name="bestandsnaam">The uploaded file's name.</param>
    /// <param name="id">The plan's identity.</param>
    /// <param name="nu">Now (UTC).</param>
    /// <returns>The plan, with its differences.</returns>
    public static ImportVoorstel Maak(
        StandenBestand bestand,
        IReadOnlyCollection<Speler> spelers,
        IReadOnlyCollection<Avond> avonden,
        Instellingen instellingen,
        string bestandsnaam,
        Guid id,
        DateTime nu)
    {
        ArgumentNullException.ThrowIfNull(bestand);
        ArgumentNullException.ThrowIfNull(spelers);

        var bestaand = spelers.GroupBy(s => NaamVergelijker.Gewoon(s.Naam)).ToDictionary(g => g.Key, g => g.First());
        var koppelingen = Koppel(bestand.Punten.Select(r => r.Naam).ToList(), bestand.Pitten.Select(r => r.Naam).ToList());

        var importSpelers = bestand.Punten.Select((rij, i) =>
        {
            var gevonden = bestaand.TryGetValue(NaamVergelijker.Gewoon(rij.Naam), out var speler);
            return new ImportSpeler
            {
                Naam = rij.Naam,
                PitNaam = koppelingen[i].PitNaam,
                Koppeling = koppelingen[i].Koppeling,
                SpelerId = gevonden ? speler.Id : Guid.NewGuid(),
                IsNieuw = !gevonden,
            };
        }).ToList();

        var invaller = bestaand.TryGetValue(NaamVergelijker.Gewoon(InvallerNaam), out var bekend) ? bekend : null;
        var heeftInvallers = bestand.Invallers.Count > 0;

        var voorstel = new ImportVoorstel
        {
            Id = id,
            Bestandsnaam = bestandsnaam ?? string.Empty,
            GemaaktOp = nu,
            Bestand = bestand,
            Spelers = importSpelers,
            InvallerId = !heeftInvallers ? Guid.Empty : invaller?.Id ?? Guid.NewGuid(),
            InvallerIsNieuw = heeftInvallers && invaller is null,
        };

        return MetVerschillen(voorstel, avonden, instellingen);
    }

    /// <summary>Couples a `Stand` row to another `pit` row (or to none), as Dad chose.</summary>
    /// <param name="voorstel">The plan.</param>
    /// <param name="naam">The `Stand` row's name.</param>
    /// <param name="pitNaam">The `pit` row's name; empty for none. A row coupled elsewhere moves here.</param>
    /// <param name="avonden">The evenings already in the app.</param>
    /// <param name="instellingen">The settings.</param>
    /// <returns>The plan with its differences recomputed.</returns>
    public static ImportVoorstel Herkoppel(ImportVoorstel voorstel, string naam, string pitNaam, IReadOnlyCollection<Avond> avonden, Instellingen instellingen)
    {
        ArgumentNullException.ThrowIfNull(voorstel);

        var pit = pitNaam ?? string.Empty;

        ImportSpeler Nieuw(ImportSpeler s)
        {
            if (s.Naam == naam)
            {
                return s with { PitNaam = pit, Koppeling = pit.Length == 0 ? Koppeling.None : Koppeling.Handmatig };
            }

            // A pits row can belong to one player only: taking it here frees it elsewhere.
            return pit.Length > 0 && s.PitNaam == pit ? s with { PitNaam = string.Empty, Koppeling = Koppeling.None } : s;
        }

        return MetVerschillen(voorstel with { Spelers = [.. voorstel.Spelers.Select(Nieuw)] }, avonden, instellingen);
    }

    /// <summary>What applying the plan writes.</summary>
    /// <param name="voorstel">The plan.</param>
    /// <param name="instellingen">The current settings.</param>
    /// <returns>The new players, the evenings and the new settings.</returns>
    public static ImportResultaat Toepassen(ImportVoorstel voorstel, Instellingen instellingen)
    {
        ArgumentNullException.ThrowIfNull(voorstel);
        ArgumentNullException.ThrowIfNull(instellingen);

        var bestand = voorstel.Bestand;
        var pitten = bestand.Pitten.GroupBy(r => r.Naam).ToDictionary(g => g.Key, g => g.First());
        var rijen = voorstel.Spelers.Zip(bestand.Punten).ToList();

        var nieuw = voorstel.Spelers.Where(s => s.IsNieuw).Select(s => new Speler { Id = s.SpelerId, Naam = s.Naam, Soort = Soort.Lid }).ToList();
        if (voorstel.InvallerIsNieuw)
        {
            nieuw.Add(new Speler { Id = voorstel.InvallerId, Naam = InvallerNaam, Soort = Soort.Invaller });
        }

        IEnumerable<Uitslag> Uitslagen(DateOnly datum)
        {
            foreach (var (speler, rij) in rijen)
            {
                if (!rij.Op(datum, out var punten))
                {
                    continue;
                }

                var pit = speler.PitNaam.Length > 0 && pitten.TryGetValue(speler.PitNaam, out var pitRij) && pitRij.Op(datum, out var p) ? p : 0;

                // The sheet's convention: exactly the absence score means absent (R3), unless there are
                // pits that evening: then it was a played 4200 that was not typed as 4199 or 4201.
                yield return punten == instellingen.PuntenAfwezig && pit == 0
                    ? new Uitslag { SpelerId = speler.SpelerId, Status = Status.Afwezig }
                    : new Uitslag { SpelerId = speler.SpelerId, Status = Status.Speelt, Punten = punten, Pitten = pit };
            }

            var invaller = bestand.Invallers.FirstOrDefault(w => w.Datum == datum);
            if (invaller is not null && voorstel.InvallerId != Guid.Empty)
            {
                yield return new Uitslag { SpelerId = voorstel.InvallerId, Status = Status.Speelt, Punten = invaller.Getal };
            }
        }

        var avonden = bestand.Avonden.Select(d => Avond.Van(d, Uitslagen(d))).ToList();

        var vrij = instellingen.GeenSpeelavonden
            .Concat(bestand.GeenSpeelavonden.Where(g => !instellingen.IsGeenSpeelavond(g.Datum) && !bestand.Avonden.Contains(g.Datum)))
            .OrderBy(g => g.Datum)
            .ToList();

        var dagen = bestand.Avonden.Concat(bestand.GeenSpeelavonden.Select(g => g.Datum)).ToList();

        return new ImportResultaat
        {
            NieuweSpelers = nieuw,
            Avonden = avonden,
            Instellingen = instellingen with
            {
                GeenSpeelavonden = vrij,
                EersteAvond = dagen.Count == 0 ? instellingen.EersteAvond : dagen.Min(),
            },
        };
    }

    /// <summary>Couples each `Stand` name to a `pit` name: the same name first, then one that looks like it.</summary>
    /// <param name="standNamen">The `Stand` names, in order.</param>
    /// <param name="pitNamen">The `pit` names.</param>
    /// <returns>Per `Stand` name, the coupled `pit` name and how.</returns>
    internal static List<(string PitNaam, Koppeling Koppeling)> Koppel(IReadOnlyList<string> standNamen, IReadOnlyList<string> pitNamen)
    {
        var uitkomst = standNamen.Select(_ => (PitNaam: string.Empty, Koppeling: Koppeling.None)).ToList();
        var vrij = pitNamen.ToList();

        for (var i = 0; i < standNamen.Count; i++)
        {
            var gelijk = vrij.FindIndex(p => NaamVergelijker.Gewoon(p) == NaamVergelijker.Gewoon(standNamen[i]));
            if (gelijk >= 0)
            {
                uitkomst[i] = (vrij[gelijk], Koppeling.Precies);
                vrij.RemoveAt(gelijk);
            }
        }

        for (var i = 0; i < standNamen.Count; i++)
        {
            if (uitkomst[i].Koppeling != Koppeling.None || vrij.Count == 0)
            {
                continue;
            }

            var kandidaten = vrij.Select(p => (Naam: p, Score: NaamVergelijker.Gelijkenis(standNamen[i], p))).OrderByDescending(k => k.Score).ToList();
            var beste = kandidaten[0];
            var duidelijk = kandidaten.Count == 1 || kandidaten[1].Score < beste.Score - 0.05;

            if (beste.Score >= NaamVergelijker.Drempel && duidelijk)
            {
                uitkomst[i] = (beste.Naam, Koppeling.LijktOp);
                vrij.Remove(beste.Naam);
            }
        }

        return uitkomst;
    }

    private static ImportVoorstel MetVerschillen(ImportVoorstel voorstel, IReadOnlyCollection<Avond> avonden, Instellingen instellingen)
    {
        var bestand = voorstel.Bestand;
        var resultaat = Toepassen(voorstel, instellingen);

        // The same rules as the live standings, over the workbook's evenings only, every row as a member in the stand.
        var vergelijk = voorstel.Spelers.Select(s => new Speler { Id = s.SpelerId, Naam = s.Naam, Soort = Soort.Lid });
        var stand = StandBerekening.Bereken(vergelijk, resultaat.Avonden, instellingen, bestand.Jaar);
        var perId = stand.Punten.ToDictionary(r => r.Speler.Id);
        var pitten = bestand.Pitten.GroupBy(r => r.Naam).ToDictionary(g => g.Key, g => g.First());

        var verschillen = new List<Verschil>();

        foreach (var (speler, rij) in voorstel.Spelers.Zip(bestand.Punten))
        {
            var regel = perId.TryGetValue(speler.SpelerId, out var r) ? r : new StandRegel();

            if (regel.Totaal != rij.Totaal)
            {
                verschillen.Add(new Verschil { Soort = VerschilSoort.TotaalPunten, Wie = speler.Naam, Excel = rij.Totaal, Programma = regel.Totaal });
            }

            if (speler.PitNaam.Length > 0 && pitten.TryGetValue(speler.PitNaam, out var pitRij) && regel.Pitten != pitRij.Totaal)
            {
                verschillen.Add(new Verschil { Soort = VerschilSoort.TotaalPitten, Wie = speler.Naam, Excel = pitRij.Totaal, Programma = regel.Pitten });
            }
        }

        foreach (var week in bestand.WeekTotalenPitten)
        {
            if (stand.WeekTotalenPitten.TryGetValue(week.Datum, out var opgeteld) && opgeteld != week.Getal)
            {
                verschillen.Add(new Verschil { Soort = VerschilSoort.WeekTotaalPitten, Datum = week.Datum, Excel = week.Getal, Programma = opgeteld });
            }
        }

        var importDatums = bestand.Avonden.ToHashSet();

        // Pits on a date the points sheet has no evening for are not imported: say so, once per date.
        verschillen.AddRange(bestand.Pitten
            .SelectMany(r => r.Waarden)
            .Where(w => w.Datum.Year == bestand.Jaar && w.Getal != 0 && !importDatums.Contains(w.Datum))
            .GroupBy(w => w.Datum)
            .OrderBy(g => g.Key)
            .Select(g => new Verschil { Soort = VerschilSoort.PittenZonderAvond, Datum = g.Key, Excel = g.Sum(w => w.Getal) }));

        var gekoppeld = voorstel.Spelers.Select(s => s.PitNaam).Where(p => p.Length > 0).ToHashSet();

        return voorstel with
        {
            Verschillen = verschillen,
            LossePitNamen = [.. bestand.Pitten.Select(p => p.Naam).Where(p => !gekoppeld.Contains(p))],
            VervangenAvonden = avonden.Count(a => importDatums.Contains(a.Datum)),
        };
    }
}

/// <summary>What applying a plan writes.</summary>
public sealed record ImportResultaat
{
    /// <summary>The players to add.</summary>
    public IReadOnlyList<Speler> NieuweSpelers { get; init; } = [];

    /// <summary>The evenings to store (replacing any on the same date).</summary>
    public IReadOnlyList<Avond> Avonden { get; init; } = [];

    /// <summary>The settings with the evenings without play and the season's first evening.</summary>
    public Instellingen Instellingen { get; init; } = new Instellingen();
}
