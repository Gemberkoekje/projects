namespace Kluskat.application.Startvulling;

/// <summary>
/// The staggered fresh start. Seeding every klus as "due now" would be honest about the old sheet
/// but useless; instead each klus gets a first due date spread across its own interval, so the
/// list trickles in from day one.
/// </summary>
public static class Spreiding
{
    /// <summary>Chooses a last-done moment per klus.</summary>
    /// <param name="intervallen">The interval in days of each klus, in seed order.</param>
    /// <param name="nu">The current moment (UTC).</param>
    /// <returns>Per klus, in the same order, the last-done moment (UTC) that makes it first due on its staggered day.</returns>
    /// <remarks>
    /// Klussen sharing an interval are spread evenly over it in seed order: ten weekly klussen land
    /// on days 0, 0, 1, 2, 2, 3, 4, 4, 5 and 6. The first of each group is due today, so there is
    /// always something to start with.
    /// </remarks>
    public static IReadOnlyList<DateTime> LaatstGedaan(IReadOnlyList<int> intervallen, DateTime nu)
    {
        ArgumentNullException.ThrowIfNull(intervallen);

        var vandaag = Kalender.Datum(nu);
        var uitkomst = new DateTime[intervallen.Count];

        foreach (var groep in intervallen.Select((dagen, index) => (Dagen: dagen, Index: index)).GroupBy(x => x.Dagen))
        {
            var leden = groep.ToList();

            for (var plek = 0; plek < leden.Count; plek++)
            {
                var eersteKeer = vandaag.AddDays(plek * groep.Key / leden.Count);
                uitkomst[leden[plek].Index] = Kalender.Middernacht(eersteKeer).AddDays(-groep.Key);
            }
        }

        return uitkomst;
    }
}
