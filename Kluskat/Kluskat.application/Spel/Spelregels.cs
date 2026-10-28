namespace Kluskat.application.Spel;

/// <summary>
/// The game rules. Pure: XP, the level, the cat's growth, the weekly streak and every achievement
/// are a function of the completion log, so changing a rule (or adding a badge) re-scores the whole
/// history, and nothing has to be migrated.
/// </summary>
public static class Spelregels
{
    /// <summary>How many klussen make a week count for the streak.</summary>
    public const int KlussenPerWeek = 3;

    /// <summary>Extra XP for the first klus of a (local) day.</summary>
    public const int BonusEersteVanDeDag = 5;

    /// <summary>Every this many streak weeks earn a freeze.</summary>
    public const int ReeksPerBevriezing = 4;

    /// <summary>The most freezes you can hold.</summary>
    public const int MaxBevriezingen = 2;

    /// <summary>A pause at least this long, then a klus, earns "Welkom terug".</summary>
    public static TimeSpan ComebackPauze { get; } = TimeSpan.FromDays(14);

    /// <summary>The wardrobe: which accessory unlocks at which level.</summary>
    public static IReadOnlyList<(int Level, Accessoire Accessoire)> Garderobe { get; } =
    [
        (2, Accessoire.Feestmuts),
        (4, Accessoire.Strikje),
        (6, Accessoire.Bril),
        (8, Accessoire.Bloem),
        (10, Accessoire.Sjaal),
        (12, Accessoire.Koksmuts),
        (15, Accessoire.Kroon),
    ];

    /// <summary>The XP a klus of a size is worth.</summary>
    /// <param name="grootte">The size.</param>
    /// <returns>10, 25 or 50.</returns>
    public static int Basis(Grootte grootte) => grootte switch
    {
        Grootte.Klein => 10,
        Grootte.Middel => 25,
        Grootte.Groot => 50,
        _ => 0,
    };

    /// <summary>The XP one completion earns, itemised for the win screen.</summary>
    /// <param name="voltooiing">The completion.</param>
    /// <param name="eersteVanDeDag">Whether it was the first of its local day.</param>
    /// <returns>The base, the bravery bonus (+50%) and the first-of-the-day bonus.</returns>
    public static XpRegel Xp(Voltooiing voltooiing, bool eersteVanDeDag)
    {
        ArgumentNullException.ThrowIfNull(voltooiing);

        var basis = Basis(voltooiing.Grootte);
        return new XpRegel
        {
            Basis = basis,
            Dapper = voltooiing.Dapper ? basis / 2 : 0,
            EersteVanDeDag = eersteVanDeDag ? BonusEersteVanDeDag : 0,
        };
    }

    /// <summary>
    /// The XP a level takes to complete: 60 for the first, 20 more for each next one. A level every
    /// few days at first, a few weeks later on.
    /// </summary>
    /// <param name="level">The level, from 1.</param>
    /// <returns>The XP from the start of that level to the next.</returns>
    public static int XpVoorLevel(int level) => 60 + (20 * (level - 1));

    /// <summary>How grown-up the cat is at a level.</summary>
    /// <param name="level">The level.</param>
    /// <returns>Kitten below 5, cat below 15, majesty from there.</returns>
    public static KatVorm Vorm(int level) => level switch
    {
        < 5 => KatVorm.Kitten,
        < 15 => KatVorm.Kat,
        _ => KatVorm.Majesteit,
    };

    /// <summary>Scores the whole log.</summary>
    /// <param name="log">Every completion, in any order.</param>
    /// <param name="ruimtes">The rooms of the active klussen, for "Rondje huis".</param>
    /// <param name="nu">The current moment (UTC), which decides what "this week" is.</param>
    /// <returns>The game as it stands.</returns>
    public static Spelstand Bereken(IEnumerable<Voltooiing> log, IReadOnlyCollection<string> ruimtes, DateTime nu)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(ruimtes);

        var geordend = log.OrderBy(v => v.Op).ToList();

        var xp = 0;
        var dagen = new HashSet<DateOnly>();
        foreach (var voltooiing in geordend)
        {
            xp += Xp(voltooiing, dagen.Add(Kalender.Datum(voltooiing.Op))).Totaal;
        }

        var (level, inLevel) = Niveau(xp);
        var reeks = Reeks.Bereken(geordend, nu);
        var statistiek = Tellen(geordend, ruimtes, reeks.Langste);

        return new Spelstand
        {
            Xp = xp,
            Level = level,
            XpInLevel = inLevel,
            XpVoorLevel = XpVoorLevel(level),
            AantalKlussen = geordend.Count,
            DezeWeek = reeks.DezeWeek,
            Reeks = reeks.Huidig,
            LangsteReeks = reeks.Langste,
            Bevriezingen = reeks.Bevriezingen,
            Trofeeen = [.. Trofeeen.Alle.Where(t => t.Behaald(statistiek))],
        };
    }

    /// <summary>What one completion brought: the score before and after it, and its own XP.</summary>
    /// <param name="log">Every completion, this one included.</param>
    /// <param name="deze">The completion to celebrate.</param>
    /// <param name="ruimtes">The rooms of the active klussen.</param>
    /// <param name="nu">The current moment (UTC).</param>
    /// <returns>The reward.</returns>
    public static Beloning Beloning(IReadOnlyCollection<Voltooiing> log, Voltooiing deze, IReadOnlyCollection<string> ruimtes, DateTime nu)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(deze);

        var zonder = log.Where(v => v.Id != deze.Id).ToList();
        var dag = Kalender.Datum(deze.Op);
        var eersteVanDeDag = !zonder.Exists(v => v.Op < deze.Op && Kalender.Datum(v.Op) == dag);

        return new Beloning
        {
            Xp = Xp(deze, eersteVanDeDag),
            Voor = Bereken(zonder, ruimtes, nu),
            Na = Bereken(log, ruimtes, nu),
        };
    }

    /// <summary>Splits total XP into a level and the XP within it.</summary>
    internal static (int Level, int InLevel) Niveau(int xp)
    {
        var level = 1;
        var rest = xp;

        while (rest >= XpVoorLevel(level))
        {
            rest -= XpVoorLevel(level);
            level++;
        }

        return (level, rest);
    }

    private static Statistiek Tellen(List<Voltooiing> geordend, IReadOnlyCollection<string> ruimtes, int langsteReeks)
    {
        var lokaal = geordend.Select(v => Kalender.Lokaal(v.Op)).ToList();
        var datums = lokaal.Select(DateOnly.FromDateTime).ToHashSet();
        var gedaan = geordend.Select(v => v.Ruimte).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new Statistiek
        {
            Totaal = geordend.Count,
            Klein = geordend.Count(v => v.Grootte == Grootte.Klein),
            Groot = geordend.Count(v => v.Grootte == Grootte.Groot),
            Dapper = geordend.Exists(v => v.Dapper),
            MeesteKeerOvergeslagen = geordend.Count == 0 ? 0 : geordend.Max(v => v.KeerOvergeslagen),
            Comeback = geordend.Zip(geordend.Skip(1)).Any(paar => paar.Second.Op - paar.First.Op >= ComebackPauze),
            OveralGeweest = ruimtes.Count > 0 && ruimtes.All(gedaan.Contains),
            MeesteOpEenDag = datums.Count == 0 ? 0 : lokaal.GroupBy(DateOnly.FromDateTime).Max(g => g.Count()),
            HeelWeekend = datums.Any(d => d.DayOfWeek == DayOfWeek.Saturday && datums.Contains(d.AddDays(1))),
            VroegeVogel = lokaal.Exists(t => t.Hour is >= 4 and < 8),
            Nachtbraker = lokaal.Exists(t => t.Hour is >= 23 or < 4),
            Jaarklus = geordend.Exists(v => v.IntervalDagen >= 365),
            VoorDeKatten = geordend.Count(VoorDeKatten),
            Planten = geordend.Count(v => string.Equals(v.Ruimte, "Planten", StringComparison.OrdinalIgnoreCase)),
            LangsteReeks = langsteReeks,
        };
    }

    private static bool VoorDeKatten(Voltooiing voltooiing)
        => string.Equals(voltooiing.Ruimte, "Huisdieren", StringComparison.OrdinalIgnoreCase)
            || voltooiing.Ruimte.Contains("katten", StringComparison.OrdinalIgnoreCase)
            || voltooiing.Naam.Contains("katten", StringComparison.OrdinalIgnoreCase);
}
