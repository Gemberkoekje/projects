using System.Globalization;

namespace Klaverjas.core.Tafels;

/// <summary>
/// The table plans for 4 to 28 players (D13): Dad's own, from the score-sheet tabs of his evening
/// workbook ("16p4ta" and so on, the sheets he prints each Monday), with the workbook's slips repaired.
/// The numbers his workbook has no tab for are made from his base plans by his own rule: with s extra
/// players, round r's slapers are numbers (r-1)s+1 to rs, and the extra numbers take their places;
/// where that would seat the same partners twice (or, from 16 players up, the same people at one table
/// twice), the fewest swaps in a round that avoid it. Seven players has no plan.
/// </summary>
public static class Tafelschemas
{
    /// <summary>The fewest players for one table.</summary>
    public const int Minimum = 4;

    /// <summary>The most players the club has planned for.</summary>
    public const int Maximum = 28;

    /// <summary>
    /// Each plan: the three rounds split by " / ", the tables by ", ", and per table the four seat numbers as in
    /// Dad's sheet, read row by row, which is the order around the table: the writer, the first dealer, the first
    /// player, the fourth (Wij 1, Zij 1, Wij 2, Zij 2). The tables are numbered in this order.
    /// </summary>
    private static readonly Dictionary<int, string> Indelingen = new Dictionary<int, string>
    {
        // Dad
        [4] = "1 2 3 4 / 1 3 4 2 / 1 4 2 3",
        // from 4, one swap
        [5] = "4 2 3 5 / 1 3 4 5 / 1 4 2 5",
        // from 4
        [6] = "5 6 3 4 / 1 5 6 2 / 1 4 2 3",
        // Dad (sheet Tafelindeling; round 2 table 2 had seat 1 twice, 2 is the one missing)
        [8] = "1 2 3 4, 5 6 7 8 / 1 6 8 5, 7 3 2 4 / 1 8 6 3, 7 2 4 5",
        // from 8
        [9] = "9 2 3 4, 5 6 7 8 / 1 6 8 5, 7 3 9 4 / 1 8 6 9, 7 2 4 5",
        // from 8
        [10] = "9 10 3 4, 5 6 7 8 / 1 6 8 5, 7 9 2 10 / 1 8 10 3, 7 2 4 9",
        // from 8, one swap
        [11] = "4 10 11 9, 5 6 7 8 / 1 11 8 10, 7 3 2 9 / 1 11 6 3, 10 2 4 5",
        // Dad
        [12] = "1 2 3 4, 5 6 7 8, 9 10 11 12 / 1 5 12 9, 7 10 4 3, 2 8 6 11 / 1 7 8 10, 4 6 12 3, 11 9 5 2",
        // from 12, one swap
        [13] = "13 2 3 4, 5 6 7 8, 9 10 11 12 / 13 5 12 9, 7 10 4 3, 1 8 6 11 / 1 7 8 10, 4 6 12 13, 11 9 5 2",
        // from 12
        [14] = "13 14 3 4, 5 6 7 8, 9 10 11 12 / 1 5 12 9, 7 10 14 13, 2 8 6 11 / 1 7 8 10, 4 14 12 3, 11 9 13 2",
        // from 12, one swap
        [15] = "13 14 15 4, 5 6 7 8, 9 10 11 12 / 2 14 12 9, 7 10 13 3, 1 8 15 11 / 1 13 14 10, 4 6 12 3, 11 15 5 2",
        // Dad
        [16] = "1 2 3 4, 5 6 7 8, 9 10 11 12, 13 14 15 16 / 1 9 5 13, 2 10 6 14, 3 11 7 15, 4 12 8 16 / 1 8 11 14, 2 5 12 15, 3 6 9 16, 4 7 10 13",
        // Dad
        [17] = "17 2 3 4, 5 6 7 8, 9 10 11 12, 13 14 15 16 / 1 9 5 13, 3 10 6 14, 17 11 7 15, 4 12 8 16 / 1 8 11 14, 2 5 12 15, 17 6 9 16, 4 7 10 13",
        // Dad (round 1 and round 2 table 1 pointed at the wrong column; by his rule and the seats left)
        [18] = "17 18 3 4, 5 6 7 8, 9 10 11 12, 13 14 15 16 / 1 9 5 13, 2 10 6 14, 17 12 7 15, 18 11 8 16 / 3 7 11 13, 1 18 12 14, 9 17 2 16, 4 8 10 15",
        // from 16, two swaps
        [19] = "8 18 19 4, 5 6 7 17, 9 10 11 12, 13 14 15 16 / 1 9 8 13, 2 10 19 14, 3 11 7 15, 17 12 18 16 / 1 18 11 14, 2 5 12 15, 3 6 19 16, 4 17 10 13",
        // Dad (seat 1 was a typed name)
        [20] = "1 2 3 4, 5 6 7 8, 9 10 11 12, 13 14 15 16, 17 18 19 20 / 14 17 12 7, 1 18 11 16, 2 5 15 20, 6 9 4 19, 10 13 3 8 / 6 13 11 20, 10 15 4 17, 1 14 8 19, 5 18 3 12, 2 9 7 16",
        // Dad
        [21] = "21 2 3 4, 5 6 7 8, 9 10 11 12, 13 14 15 16, 17 18 19 20 / 14 17 12 7, 1 18 11 16, 21 5 15 20, 6 9 4 19, 10 13 3 8 / 5 13 11 19, 9 15 8 17, 6 14 1 21, 4 16 20 12, 7 10 2 18",
        // Dad (round 3 table 5 had 15 twice, 9 is the one missing)
        [22] = "21 22 3 4, 5 6 7 8, 9 10 11 12, 13 14 15 16, 17 18 19 20 / 14 17 12 7, 1 18 11 16, 2 5 15 20, 6 9 22 19, 10 13 21 8 / 22 13 11 20, 10 15 4 17, 1 14 8 19, 21 18 2 12, 3 9 7 16",
        // from 20, two swaps
        [23] = "5 22 23 4, 21 6 7 8, 9 10 11 12, 13 14 15 16, 17 18 19 20 / 14 17 12 21, 1 18 11 16, 2 22 15 20, 23 9 7 19, 10 13 3 8 / 6 13 11 20, 10 15 4 17, 1 14 22 19, 5 18 3 12, 2 23 21 16",
        // Dad
        [24] = "1 2 3 4, 5 6 7 8, 9 10 11 12, 13 14 15 16, 17 18 19 20, 21 22 23 24 / 1 9 5 14, 3 18 7 22, 2 19 6 23, 10 4 13 8, 17 12 21 16, 11 20 15 24 / 16 3 19 24, 14 4 21 11, 12 7 13 23, 9 6 18 15, 8 1 17 22, 2 10 5 20",
        // Dad
        [25] = "25 2 3 4, 5 6 7 8, 9 10 11 12, 13 14 15 16, 17 18 19 20, 21 22 23 24 / 1 9 5 14, 3 18 7 22, 25 19 6 23, 10 4 13 8, 17 12 21 16, 11 20 15 24 / 10 25 14 24, 19 4 21 11, 12 7 13 23, 9 6 18 15, 8 1 17 22, 2 16 5 20",
        // Dad
        [26] = "25 26 3 4, 5 6 7 8, 9 10 11 12, 13 14 15 16, 17 18 19 20, 21 22 23 24 / 1 9 5 14, 25 18 7 22, 2 19 6 23, 10 26 13 8, 17 12 21 16, 11 20 15 24 / 16 3 19 24, 14 4 21 11, 12 7 13 23, 9 26 18 15, 8 1 17 22, 2 10 25 20",
        // Dad (26 twice and #REF!: 27 is the one missing)
        [27] = "25 26 27 4, 5 6 7 8, 9 10 11 12, 13 14 15 16, 17 18 19 20, 21 22 23 24 / 1 9 26 14, 3 18 7 22, 2 19 27 23, 10 25 13 8, 17 12 21 16, 11 20 15 24 / 16 3 19 24, 14 25 21 11, 12 4 13 23, 27 6 18 15, 26 2 17 22, 1 10 5 20",
        // Dad (#REF!: 27 and 28 are the ones missing)
        [28] = "1 2 3 4, 5 6 7 8, 9 10 11 12, 13 14 15 16, 17 18 19 20, 21 22 23 24, 25 26 27 28 / 1 10 27 22, 3 7 11 26, 2 5 19 24, 4 6 17 13, 8 9 15 28, 14 18 12 21, 23 16 25 20 / 6 16 12 27, 3 5 22 25, 7 2 28 17, 10 15 18 23, 4 8 24 14, 9 20 21 26, 13 1 19 11",
    };

    private static readonly Dictionary<int, Tafelschema> Plannen = Indelingen.ToDictionary(kv => kv.Key, kv => Lees(kv.Key, kv.Value));

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

        return Plannen[aantal];
    }

    private static Tafelschema Lees(int aantal, string indeling)
    {
        var rondes = indeling.Split(" / ")
            .Select(ronde => ronde.Split(", ")
                .Select(tafel => tafel.Split(' ').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList())
                .Select(s => new Tafel { Wij1 = s[0], Zij1 = s[1], Wij2 = s[2], Zij2 = s[3] })
                .ToList())
            .ToList();

        var samen = rondes.SelectMany(r => r).SelectMany(t => Paren(t.Stoelen)).ToList();

        return new Tafelschema
        {
            Aantal = aantal,
            NooitTweeKeerSamen = samen.Count == samen.Distinct().Count(),
            Rondes = [.. rondes.Select(tafels => new Ronde
            {
                Tafels = tafels,
                Stil = [.. Enumerable.Range(1, aantal).Except(tafels.SelectMany(t => t.Stoelen))],
            })],
        };
    }

    private static IEnumerable<(int, int)> Paren(IReadOnlyList<int> stoelen)
    {
        for (var i = 0; i < stoelen.Count; i++)
        {
            for (var j = i + 1; j < stoelen.Count; j++)
            {
                yield return stoelen[i] < stoelen[j] ? (stoelen[i], stoelen[j]) : (stoelen[j], stoelen[i]);
            }
        }
    }
}
