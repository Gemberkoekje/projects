namespace Kluskat.application.Kiezen;

/// <summary>
/// Picks the one klus to show. Pure: everything it needs comes in as arguments, so the whole rule
/// set is unit-tested without a database.
/// </summary>
/// <remarks>
/// The order is deterministic on purpose. A skipped klus is not punished or reshuffled; it is only
/// hidden for the skip window, and when that wears off it is simply the most-behind klus again.
/// </remarks>
public static class KlusKiezer
{
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

    /// <summary>Most behind first; ties go to the smaller klus, then the one waiting longest.</summary>
    private static List<KlusOverzicht> Volgorde(IEnumerable<KlusOverzicht> klussen, DateTime nu)
        => [.. klussen
            .OrderByDescending(k => k.Achterstand(nu))
            .ThenBy(k => k.Grootte)
            .ThenBy(k => k.LaatstGedaan)
            .ThenBy(k => k.Naam, StringComparer.Ordinal)];
}
