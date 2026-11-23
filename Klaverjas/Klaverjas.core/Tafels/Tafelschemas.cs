using System.Collections.Concurrent;

namespace Klaverjas.core.Tafels;

/// <summary>
/// The table plans for 4 to 28 players (D13). Dad's workbook holds no clean scheme to import (its
/// score sheets mix seat numbers, names pulled in from the last draw and broken references), so the
/// plans are computed, as in the approved mock and with its algorithm: a seeded search, so the same
/// number of players always gives the same plan. The slapers have the lowest numbers: round 1 is sat
/// out by numbers 1 up to s, round 2 by the next s, round 3 by the next s (R12).
/// </summary>
public static class Tafelschemas
{
    /// <summary>The fewest players for one table.</summary>
    public const int Minimum = 4;

    /// <summary>The most players the club has planned for.</summary>
    public const int Maximum = 28;

    private static readonly ConcurrentDictionary<int, Tafelschema> Cache = new ConcurrentDictionary<int, Tafelschema>();

    /// <summary>
    /// Every number of players a plan exists for. Seven is left out: three would have to sit out
    /// each round, nine in all, and there are only seven to go round.
    /// </summary>
    public static IReadOnlyList<int> Aantallen { get; } = [.. Enumerable.Range(Minimum, Maximum - Minimum + 1).Where(KanMet)];

    /// <summary>Whether a plan exists for a number of players.</summary>
    /// <param name="aantal">The number of players.</param>
    /// <returns><see langword="true"/> when everyone sits out at most once.</returns>
    public static bool KanMet(int aantal) => aantal is >= Minimum and <= Maximum && 3 * (aantal % 4) <= aantal;

    /// <summary>The nearest number of players a plan exists for: 4 to 28, and 8 for 7.</summary>
    /// <param name="aantal">Any number.</param>
    /// <returns>A number in <see cref="Aantallen"/>.</returns>
    public static int Dichtstbij(int aantal)
    {
        var binnen = Math.Clamp(aantal, Minimum, Maximum);
        return KanMet(binnen) ? binnen : binnen + 1;
    }

    /// <summary>The plan for a number of players.</summary>
    /// <param name="aantal">The number of players (see <see cref="Aantallen"/>).</param>
    /// <returns>The plan; always the same for the same number.</returns>
    public static Tafelschema Voor(int aantal)
    {
        if (!KanMet(aantal))
        {
            throw new ArgumentOutOfRangeException(nameof(aantal), aantal, "No table plan for this number of players.");
        }

        return Cache.GetOrAdd(aantal, Bereken);
    }

    private static Tafelschema Bereken(int aantal)
    {
        var toeval = new Toeval((uint)(aantal * 7919));
        var s = aantal % 4;
        var alle = Enumerable.Range(1, aantal).ToList();
        var stil = Enumerable.Range(0, 3).Select(r => alle.Skip(r * s).Take(s).ToList()).ToList();
        var spelend = stil.Select(st => alle.Where(x => !st.Contains(x)).ToList()).ToList();
        var tafels = aantal / 4;

        foreach (var streng in tafels >= 4 ? new[] { true, false } : new[] { false })
        {
            for (var poging = 0; poging < 40; poging++)
            {
                var plan = Probeer(spelend, streng, toeval);
                if (plan.Count == 3)
                {
                    return Schema(aantal, plan, stil, streng);
                }
            }
        }

        // Not reached for 4–28 players (the specs check every number); kept so a plan always exists.
        return Schema(aantal, [.. spelend.Select(p => Vieren(Schud([.. p], toeval)))], stil, nooitTweeKeer: false);
    }

    private static List<List<List<int>>> Probeer(List<List<int>> spelend, bool streng, Toeval toeval)
    {
        var ontmoet = new HashSet<(int, int)>();
        var maten = new HashSet<(int, int)>();
        var plan = new List<List<List<int>>>();

        foreach (var ronde in spelend)
        {
            var tafels = Ronde(ronde, ontmoet, maten, streng, toeval);
            if (tafels.Count == 0)
            {
                return [];
            }

            foreach (var g in tafels)
            {
                for (var i = 0; i < 4; i++)
                {
                    for (var j = i + 1; j < 4; j++)
                    {
                        ontmoet.Add(Paar(g[i], g[j]));
                    }
                }

                maten.Add(Paar(g[0], g[1]));
                maten.Add(Paar(g[2], g[3]));
            }

            plan.Add(tafels);
        }

        return plan;
    }

    /// <summary>
    /// Seats one round: each table starts with the next player of a shuffled list; the partner and
    /// the others are whoever has met the fewest of those already seated.
    /// </summary>
    private static List<List<int>> Ronde(List<int> spelend, HashSet<(int, int)> ontmoet, HashSet<(int, int)> maten, bool streng, Toeval toeval)
    {
        for (var poging = 0; poging < 400; poging++)
        {
            var pot = Schud([.. spelend], toeval);
            var tafels = new List<List<int>>();
            var gelukt = true;

            while (pot.Count > 0 && gelukt)
            {
                var g = new List<int> { pot[0] };
                pot.RemoveAt(0);
                gelukt = Vul(g, pot, ontmoet, maten, streng);
                tafels.Add(g);
            }

            if (gelukt)
            {
                return tafels;
            }
        }

        return [];
    }

    private static bool Vul(List<int> g, List<int> pot, HashSet<(int, int)> ontmoet, HashSet<(int, int)> maten, bool streng)
    {
        while (g.Count < 4)
        {
            var beste = Beste(g, pot, ontmoet, maten, streng);
            if (beste < 0)
            {
                return false;
            }

            g.Add(pot[beste]);
            pot.RemoveAt(beste);
        }

        return true;
    }

    /// <summary>The next to seat: who has met the fewest of those already at the table, never a former partner as partner.</summary>
    /// <returns>The index in the pot, or -1 when nobody fits.</returns>
    private static int Beste(List<int> g, List<int> pot, HashSet<(int, int)> ontmoet, HashSet<(int, int)> maten, bool streng)
    {
        // The second seat is the first one's partner, the fourth the third one's.
        var maatVan = g.Count switch
        {
            1 => g[0],
            3 => g[2],
            _ => 0,
        };

        var beste = -1;
        var besteKosten = int.MaxValue;

        for (var i = 0; i < pot.Count && besteKosten > 0; i++)
        {
            var c = pot[i];
            var kosten = g.Count(x => ontmoet.Contains(Paar(x, c)));
            var verboden = (streng && kosten > 0) || (maatVan != 0 && maten.Contains(Paar(maatVan, c)));

            if (!verboden && kosten < besteKosten)
            {
                besteKosten = kosten;
                beste = i;
            }
        }

        return beste;
    }

    private static Tafelschema Schema(int aantal, List<List<List<int>>> plan, List<List<int>> stil, bool nooitTweeKeer) => new Tafelschema
    {
        Aantal = aantal,
        NooitTweeKeerSamen = nooitTweeKeer,
        Rondes = [.. plan.Select((tafels, r) => new Ronde
        {
            Tafels = [.. tafels.Select(g => new Tafel { Wij1 = g[0], Wij2 = g[1], Zij1 = g[2], Zij2 = g[3] })],
            Stil = stil[r],
        })],
    };

    private static List<List<int>> Vieren(List<int> spelers)
        => [.. Enumerable.Range(0, spelers.Count / 4).Select(t => spelers.Skip(t * 4).Take(4).ToList())];

    private static (int, int) Paar(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>Fisher–Yates from the end, as in the mock.</summary>
    private static List<int> Schud(List<int> lijst, Toeval toeval)
    {
        for (var i = lijst.Count - 1; i > 0; i--)
        {
            var j = (int)Math.Floor(toeval.Volgende() * (i + 1));
            (lijst[i], lijst[j]) = (lijst[j], lijst[i]);
        }

        return lijst;
    }

    /// <summary>The mock's seeded random numbers (mulberry32), so a number of players always gets the same plan.</summary>
    private sealed class Toeval(uint zaad)
    {
        private uint staat = zaad;

        public double Volgende()
        {
            unchecked
            {
                staat += 0x6D2B79F5;
                var t = (staat ^ (staat >> 15)) * (1 | staat);
                t = (t + ((t ^ (t >> 7)) * (61 | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }
}
