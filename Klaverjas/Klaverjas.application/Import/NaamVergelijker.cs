using System.Globalization;
using System.Text;

namespace Klaverjas.application.Import;

/// <summary>Compares names the way a person would: capitals, accents, dots and spacing don't count.</summary>
public static class NaamVergelijker
{
    /// <summary>How alike two names must be to be coupled without Dad choosing (one letter in a name of twelve).</summary>
    public const double Drempel = 0.85;

    /// <summary>A name without capitals, accents, punctuation or double spaces.</summary>
    /// <param name="naam">The name.</param>
    /// <returns>The plain form.</returns>
    public static string Gewoon(string naam)
    {
        var zonderAccenten = new StringBuilder();

        foreach (var c in (naam ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                zonderAccenten.Append(char.IsLetter(c) ? char.ToLowerInvariant(c) : ' ');
            }
        }

        return string.Join(' ', zonderAccenten.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>How alike two names are.</summary>
    /// <param name="links">One name.</param>
    /// <param name="rechts">The other.</param>
    /// <returns>1 for the same plain name, down to 0 for nothing in common.</returns>
    public static double Gelijkenis(string links, string rechts)
    {
        var a = Gewoon(links);
        var b = Gewoon(rechts);
        var langste = Math.Max(a.Length, b.Length);
        return langste == 0 ? 1 : 1 - ((double)Afstand(a, b) / langste);
    }

    /// <summary>The Levenshtein distance: the fewest single-letter changes from one to the other.</summary>
    private static int Afstand(string a, string b)
    {
        var vorige = Enumerable.Range(0, b.Length + 1).ToArray();
        var huidige = new int[b.Length + 1];

        for (var i = 1; i <= a.Length; i++)
        {
            huidige[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var kosten = a[i - 1] == b[j - 1] ? 0 : 1;
                huidige[j] = Math.Min(Math.Min(huidige[j - 1] + 1, vorige[j] + 1), vorige[j - 1] + kosten);
            }

            (vorige, huidige) = (huidige, vorige);
        }

        return vorige[b.Length];
    }
}
