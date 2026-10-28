namespace Kluskat.application.Spel;

/// <summary>An achievement: a name, what earns it, and the rule that checks it.</summary>
public sealed record Trofee
{
    /// <summary>Stable identity; never rename one, or earned badges would seem to vanish.</summary>
    required public string Id { get; init; }

    /// <summary>The Dutch name on the badge.</summary>
    required public string Naam { get; init; }

    /// <summary>What earns it, in one Dutch sentence.</summary>
    required public string Omschrijving { get; init; }

    /// <summary>The rule. Internal: callers ask <see cref="Spelregels"/> for the earned set.</summary>
    internal Func<Statistiek, bool> Behaald { get; init; } = _ => false;
}

/// <summary>
/// Every achievement there is. Only gains: nothing here punishes a gap, and one ("Welkom terug")
/// rewards coming back after one.
/// </summary>
public static class Trofeeen
{
    /// <summary>All achievements, in display order.</summary>
    public static IReadOnlyList<Trofee> Alle { get; } =
    [
        Nieuw("eerste", "Eerste klus", "Je allereerste klusje gedaan.", s => s.Totaal >= 1),
        Nieuw("tien", "Op dreef", "10 klussen gedaan.", s => s.Totaal >= 10),
        Nieuw("vijftig", "Halve eeuw", "50 klussen gedaan.", s => s.Totaal >= 50),
        Nieuw("honderd", "Honderd!", "100 klussen gedaan.", s => s.Totaal >= 100),
        Nieuw("klusmeester", "Klusmeester", "250 klussen gedaan.", s => s.Totaal >= 250),
        Nieuw("legende", "Legende", "500 klussen gedaan.", s => s.Totaal >= 500),
        Nieuw("dapper", "Dapper", "Een klus gedaan die je kort daarvoor had overgeslagen.", s => s.Dapper),
        Nieuw("overwinnaar", "Overwinnaar", "Een klus gedaan die je drie keer of vaker had overgeslagen.", s => s.MeesteKeerOvergeslagen >= 3),
        Nieuw("welkom-terug", "Welkom terug", "Na twee weken pauze weer een klus gedaan.", s => s.Comeback),
        Nieuw("rondje-huis", "Rondje huis", "In elke ruimte iets gedaan.", s => s.OveralGeweest),
        Nieuw("lekker-bezig", "Lekker bezig", "5 klussen op één dag.", s => s.MeesteOpEenDag >= 5),
        Nieuw("weekendwerk", "Weekendwerk", "In hetzelfde weekend zaterdag én zondag een klus gedaan.", s => s.HeelWeekend),
        Nieuw("vroege-vogel", "Vroege vogel", "Een klus gedaan vóór acht uur 's ochtends.", s => s.VroegeVogel),
        Nieuw("nachtbraker", "Nachtbraker", "Een klus gedaan na elf uur 's avonds.", s => s.Nachtbraker),
        Nieuw("klein-maar-fijn", "Klein maar fijn", "25 kleine klussen gedaan.", s => s.Klein >= 25),
        Nieuw("grote-klapper", "Grote klapper", "10 grote klussen gedaan.", s => s.Groot >= 10),
        Nieuw("jaarklus", "Jaarklus", "Een klus gedaan die maar één keer per jaar hoeft.", s => s.Jaarklus),
        Nieuw("kattenvriend", "Kattenvriend", "10 keer iets voor de katten gedaan.", s => s.VoorDeKatten >= 10),
        Nieuw("groene-vingers", "Groene vingers", "10 keer de planten water gegeven.", s => s.Planten >= 10),
        Nieuw("reeks-4", "Vier op een rij", "4 weken op rij minstens 3 klussen.", s => s.LangsteReeks >= 4),
        Nieuw("reeks-12", "Een heel kwartaal", "12 weken op rij minstens 3 klussen.", s => s.LangsteReeks >= 12),
        Nieuw("reeks-52", "Een heel jaar", "52 weken op rij minstens 3 klussen.", s => s.LangsteReeks >= 52),
    ];

    private static Trofee Nieuw(string id, string naam, string omschrijving, Func<Statistiek, bool> behaald)
        => new Trofee { Id = id, Naam = naam, Omschrijving = omschrijving, Behaald = behaald };
}
