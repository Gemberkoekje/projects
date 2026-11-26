namespace Kluskat.application.Kiezen;

/// <summary>
/// Picks the one klus to show. Pure: everything it needs comes in as arguments, so the whole rule
/// set is unit-tested without a database.
/// </summary>
/// <remarks>
/// <para>
/// The most urgent klus goes first (<see cref="Urgentie"/>): the one furthest behind relative to
/// its own interval, so a weekly klus a week late outranks a yearly klus a week late. Done less
/// often than the list asks, every klus stretches by the same factor.
/// </para>
/// <para>
/// Except a klus the app has never seen done. Its "last done" is only the staggered start, so it
/// counts as a weekly klus that became due on its staggered day, and competes on days waited with
/// the weekly ones. Something nobody remembers doing comes before vacuuming the kitchen again; after
/// its first Gedaan it keeps its own interval like the rest.
/// </para>
/// <para>
/// The order is deterministic on purpose. A skipped klus is not punished or reshuffled; it is only
/// hidden for the skip window, and when that wears off it is simply the most urgent klus again.
/// </para>
/// </remarks>
public static class KlusKiezer
{
    /// <summary>The interval, in days, a klus counts as until the app has seen it done once.</summary>
    public const int NooitGedaanIntervalDagen = 7;

    /// <summary>
    /// How urgent a klus is: the time since it was last done, in its own intervals (1.0 is exactly
    /// due, 2.0 a whole interval late). A klus never done counts as a weekly klus that became due at
    /// its <see cref="KlusOverzicht.VolgendeKeer"/>.
    /// </summary>
    /// <param name="klus">The klus.</param>
    /// <param name="nu">The current moment (UTC).</param>
    /// <returns>Higher is more urgent; below 1.0 the klus is not due yet.</returns>
    public static double Urgentie(KlusOverzicht klus, DateTime nu)
    {
        ArgumentNullException.ThrowIfNull(klus);

        return klus.OoitGedaan
            ? (nu - klus.LaatstGedaan).TotalDays / klus.IntervalDagen
            : 1 + ((nu - klus.VolgendeKeer).TotalDays / NooitGedaanIntervalDagen);
    }

    /// <summary>Picks a klus.</summary>
    /// <param name="klussen">Every active klus.</param>
    /// <param name="sessie">The current skip window (already checked for expiry).</param>
    /// <param name="nu">The current moment (UTC).</param>
    /// <param name="vooruit">Whether the user asked to get ahead: offer a klus that is not due yet.</param>
    /// <returns>What to show.</returns>
    public static Keuze Kies(IEnumerable<KlusOverzicht> klussen, Sessie sessie, DateTime nu, bool vooruit)
    {
        ArgumentNullException.ThrowIfNull(klussen);
        ArgumentNullException.ThrowIfNull(sessie);

        var actief = klussen.Where(k => !k.Geschrapt).ToList();
        var beschikbaar = actief
            .Where(k => !sessie.IsOvergeslagen(k.Id) && sessie.Past(k.Grootte))
            .ToList();

        var kleinGehouden = sessie.MaxGrootte != Grootte.None;

        if (vooruit)
        {
            // Getting ahead: the klus closest to due. Everything beschikbaar is a candidate, so this
            // also picks up a due klus that appeared since the screen said "niks te doen".
            var eerstvolgende = Volgorde(beschikbaar, nu);
            return eerstvolgende.Count == 0
                ? new Keuze { Uitkomst = KeuzeUitkomst.NiksTeDoen, KleinGehouden = kleinGehouden }
                : new Keuze
                {
                    Uitkomst = KeuzeUitkomst.Klus,
                    Klus = eerstvolgende[0],
                    Vooruit = !eerstvolgende[0].IsAanDeBeurt(nu),
                    KleinGehouden = kleinGehouden,
                };
        }

        var aanDeBeurt = Volgorde(beschikbaar.Where(k => k.IsAanDeBeurt(nu)), nu);

        if (aanDeBeurt.Count > 0)
        {
            return new Keuze { Uitkomst = KeuzeUitkomst.Klus, Klus = aanDeBeurt[0], KleinGehouden = kleinGehouden };
        }

        return new Keuze
        {
            Uitkomst = actief.Exists(k => k.IsAanDeBeurt(nu)) ? KeuzeUitkomst.AllesOvergeslagen : KeuzeUitkomst.NiksTeDoen,
            KleinGehouden = kleinGehouden,
        };
    }

    /// <summary>
    /// Most urgent first (for klussen not due yet: the one closest to due); ties go to the smaller
    /// klus, then the one done longest ago.
    /// </summary>
    private static List<KlusOverzicht> Volgorde(IEnumerable<KlusOverzicht> klussen, DateTime nu)
        => [.. klussen
            .OrderByDescending(k => Urgentie(k, nu))
            .ThenBy(k => k.Grootte)
            .ThenBy(k => k.LaatstGedaan)
            .ThenBy(k => k.Naam, StringComparer.Ordinal)];
}
