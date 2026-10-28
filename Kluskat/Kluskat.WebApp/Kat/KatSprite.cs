using Microsoft.AspNetCore.Html;
using System.Globalization;
using System.Text;

namespace Kluskat.WebApp.Kat;

/// <summary>How the cat is posed on a screen.</summary>
public enum KatHouding
{
    /// <summary>Not chosen.</summary>
    None = 0,

    /// <summary>Sitting, looking at you, blinking now and then. The klus screen.</summary>
    Zit = 1,

    /// <summary>Eyes closed in a smile, mouth open, hopping. The win screen.</summary>
    Blij = 2,

    /// <summary>Eyes shut, breathing slowly. Nothing to do, or done for now.</summary>
    Slaap = 3,
}

/// <summary>
/// The cat, as pixel art. One 26×22 sprite drawn as a character grid; each pose swaps a handful of
/// face pixels. Rendered as an inline SVG of merged runs with crisp edges, so it stays sharp at any
/// size and the CSS can animate parts of it (the blink is its own group).
/// </summary>
internal static class KatSprite
{
    /// <summary>Width of the sprite in pixels; every row has exactly this many characters.</summary>
    internal const int Breedte = 26;

    /// <summary>
    /// The sitting cat. <c>o</c> outline, <c>a</c> fur, <c>b</c> fur shade, <c>s</c> stripe,
    /// <c>c</c> cream, <c>p</c> pink, <c>k</c> blush, <c>e</c> eye, <c>w</c> eye shine. The tail curls
    /// up on the right in the last four columns.
    /// </summary>
    internal static readonly IReadOnlyList<string> Basis =
    [
        "...o..............o.......",
        "..oao............oao......",
        "..oapo..........opao......",
        ".oappao........oappao.....",
        ".oappaooooooooooappao.....",
        ".oaaaaaasassasaaaaaao.....",
        "oaaaaaweasaasaweaaaaao....",
        "oaaaaaeeaaaaaaeeaaaaao....",
        "oaaaaaeeaaaaaaeeaaaaao....",
        "oaaakaacccppcccaakaaao....",
        "oaaaaaccocoococcaaaaao....",
        ".oaaaccccoccoccccaaao.....",
        "..oaaaccccccccccaaao...oo.",
        "...obaaccccccccaabo...osao",
        "..obaaaaccccccaaaabo..oabo",
        "..obasaaccccccaasabo..oabo",
        ".obaaaaaccccccaaaaabo.osso",
        ".obasaaaccccccaaasabo.oabo",
        ".obaaaaaaccccaaaaaabo.oabo",
        ".obaaaocccoocccoaaaboosso.",
        ".obbbbocccoocccobbbboabo..",
        "..ooooooooooooooooooooo...",
    ];

    private static readonly Dictionary<char, string> Kleuren = new Dictionary<char, string>
    {
        ['o'] = "#3b2417",
        ['a'] = "#f2a65a",
        ['b'] = "#d9823b",
        ['s'] = "#c46a2c",
        ['c'] = "#fff1dc",
        ['p'] = "#f49ca6",
        ['k'] = "#f7b6a0",
        ['e'] = "#2b1b12",
        ['w'] = "#ffffff",
    };

    /// <summary>Eyes shut: the open eyes become a line along their lower edge.</summary>
    private static readonly (int Rij, int Kolom, char Kleur)[] OgenDicht =
    [
        (6, 6, 'a'), (6, 7, 'a'), (6, 14, 'a'), (6, 15, 'a'),
        (7, 6, 'a'), (7, 7, 'a'), (7, 14, 'a'), (7, 15, 'a'),
        (8, 6, 'o'), (8, 7, 'o'), (8, 14, 'o'), (8, 15, 'o'),
    ];

    /// <summary>A happy face: eyes closed in two little arches, mouth open with a pink tongue.</summary>
    private static readonly (int Rij, int Kolom, char Kleur)[] BlijGezicht =
    [
        (6, 6, 'a'), (6, 7, 'a'), (6, 14, 'a'), (6, 15, 'a'),
        (7, 6, 'o'), (7, 7, 'o'), (7, 14, 'o'), (7, 15, 'o'),
        (8, 5, 'o'), (8, 6, 'a'), (8, 7, 'a'), (8, 8, 'o'),
        (8, 13, 'o'), (8, 14, 'a'), (8, 15, 'a'), (8, 16, 'o'),
        (11, 10, 'p'), (11, 11, 'p'),
        (12, 10, 'o'), (12, 11, 'o'),
    ];

    /// <summary>The cat in a pose, as markup that is safe to emit unencoded (it holds no input).</summary>
    /// <param name="houding">The pose.</param>
    /// <returns>An inline SVG.</returns>
    public static IHtmlContent Html(KatHouding houding) => new HtmlString(Svg(houding));

    /// <summary>The cat in a pose, as an SVG string.</summary>
    /// <param name="houding">The pose.</param>
    /// <returns>An inline SVG.</returns>
    internal static string Svg(KatHouding houding)
    {
        var pixels = Basis.Select(rij => rij.ToCharArray()).ToArray();

        if (houding == KatHouding.Blij)
        {
            Toepassen(pixels, BlijGezicht);
        }
        else if (houding == KatHouding.Slaap)
        {
            Toepassen(pixels, OgenDicht);
        }

        var hoogte = Basis.Count;
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg class=\"kat {Klasse(houding)}\" viewBox=\"0 0 {Breedte} {hoogte + 1}\" xmlns=\"http://www.w3.org/2000/svg\" shape-rendering=\"crispEdges\" aria-hidden=\"true\">");

        // A soft floor shadow under the paws, coloured by the theme.
        svg.Append(CultureInfo.InvariantCulture, $"<rect class=\"kat-schaduw\" x=\"3\" y=\"{hoogte}\" width=\"21\" height=\"1\"/>");
        svg.Append("<g class=\"kat-lijf\">");
        Rechthoeken(svg, pixels);

        if (houding == KatHouding.Zit)
        {
            // The blink: closed-eye pixels on top, shown for a moment by a CSS animation.
            var knipper = Basis.Select(rij => new string('.', rij.Length).ToCharArray()).ToArray();
            Toepassen(knipper, OgenDicht);
            svg.Append("<g class=\"kat-knipper\">");
            Rechthoeken(svg, knipper);
            svg.Append("</g>");
        }

        svg.Append("</g></svg>");
        return svg.ToString();
    }

    private static string Klasse(KatHouding houding) => houding switch
    {
        KatHouding.Blij => "kat-blij",
        KatHouding.Slaap => "kat-slaap",
        _ => "kat-zit",
    };

    private static void Toepassen(char[][] pixels, (int Rij, int Kolom, char Kleur)[] wijzigingen)
    {
        foreach (var (rij, kolom, kleur) in wijzigingen)
        {
            pixels[rij][kolom] = kleur;
        }
    }

    /// <summary>One rect per horizontal run of equal colour, so the SVG stays small.</summary>
    private static void Rechthoeken(StringBuilder svg, char[][] pixels)
    {
        for (var y = 0; y < pixels.Length; y++)
        {
            var x = 0;
            while (x < pixels[y].Length)
            {
                var teken = pixels[y][x];
                var start = x;
                while (x < pixels[y].Length && pixels[y][x] == teken)
                {
                    x++;
                }

                if (Kleuren.TryGetValue(teken, out var kleur))
                {
                    svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{start}\" y=\"{y}\" width=\"{x - start}\" height=\"1\" fill=\"{kleur}\"/>");
                }
            }
        }
    }
}
