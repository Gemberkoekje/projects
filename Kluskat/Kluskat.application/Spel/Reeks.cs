namespace Kluskat.application.Spel;

/// <summary>
/// The forgiving weekly streak. A week (Monday to Sunday, Amsterdam) counts with at least
/// <see cref="Spelregels.KlussenPerWeek"/> klussen. Every <see cref="Spelregels.ReeksPerBevriezing"/>
/// counted weeks earn a freeze (at most <see cref="Spelregels.MaxBevriezingen"/>), and a week that
/// does not count spends one instead of breaking the streak.
/// </summary>
/// <param name="Huidig">The streak now. The current week adds to it once it counts, and never breaks it while it is still running.</param>
/// <param name="Langste">The longest streak ever.</param>
/// <param name="Bevriezingen">Freezes in hand.</param>
/// <param name="DezeWeek">Klussen done this week.</param>
internal sealed record Reeks(int Huidig, int Langste, int Bevriezingen, int DezeWeek)
{
    /// <summary>Walks the weeks from the first completion to now.</summary>
    /// <param name="geordend">The completion log, oldest first.</param>
    /// <param name="nu">The current moment (UTC).</param>
    /// <returns>The streak.</returns>
    internal static Reeks Bereken(IReadOnlyList<Voltooiing> geordend, DateTime nu)
    {
        if (geordend.Count == 0)
        {
            return new Reeks(0, 0, 0, 0);
        }

        var dezeMaandag = Kalender.Maandag(nu);
        var perWeek = geordend.GroupBy(v => Kalender.Maandag(v.Op)).ToDictionary(g => g.Key, g => g.Count());

        var huidig = 0;
        var langste = 0;
        var bevriezingen = 0;

        for (var week = Kalender.Maandag(geordend[0].Op); week < dezeMaandag; week = week.AddDays(7))
        {
            if (perWeek.GetValueOrDefault(week) >= Spelregels.KlussenPerWeek)
            {
                Tel(ref huidig, ref bevriezingen);
            }
            else if (huidig > 0 && bevriezingen > 0)
            {
                // A freeze holds the streak; the week itself does not add to it.
                bevriezingen--;
            }
            else
            {
                huidig = 0;
            }

            langste = Math.Max(langste, huidig);
        }

        var dezeWeek = perWeek.GetValueOrDefault(dezeMaandag);
        if (dezeWeek >= Spelregels.KlussenPerWeek)
        {
            Tel(ref huidig, ref bevriezingen);
            langste = Math.Max(langste, huidig);
        }

        return new Reeks(huidig, langste, bevriezingen, dezeWeek);
    }

    private static void Tel(ref int huidig, ref int bevriezingen)
    {
        huidig++;

        if (huidig % Spelregels.ReeksPerBevriezing == 0)
        {
            bevriezingen = Math.Min(Spelregels.MaxBevriezingen, bevriezingen + 1);
        }
    }
}
