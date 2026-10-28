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
/// The cat, as pixel art. One 26×22 sprite drawn as a character grid, with layers on top: the
/// growth form (kitten eyes, the majesty's ear tufts and ruff), an accessory, and the pose's face.
/// Rendered as an inline SVG of merged runs with crisp edges, so it stays sharp at any size and the
/// CSS can animate parts of it (the blink and the sparkles are their own groups).
/// </summary>
/// <remarks>
/// Coordinates are (row, column) on the base sprite. Rows above it (down to <c>-Kop</c>) are
/// headroom for hats and tufts, so every cat has the same canvas and nothing jumps when it dresses.
/// </remarks>
internal static class KatSprite
{
    /// <summary>Width of the sprite in pixels; every row has exactly this many characters.</summary>
    internal const int Breedte = 26;

    /// <summary>Rows of headroom above the base sprite.</summary>
    internal const int Kop = 6;

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

    /// <summary>
    /// The palette. Besides the cat: <c>r</c>/<c>R</c> red, <c>y</c>/<c>Y</c> gold, <c>u</c>/<c>U</c>
    /// blue, <c>g</c>/<c>G</c> green, <c>x</c>/<c>X</c> white, <c>l</c> glass, <c>t</c> sparkle.
    /// </summary>
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
        ['r'] = "#e0464e",
        ['R'] = "#a3262f",
        ['y'] = "#f7c948",
        ['Y'] = "#c98f22",
        ['u'] = "#5b8def",
        ['U'] = "#3459b8",
        ['g'] = "#46b07a",
        ['G'] = "#2b7d52",
        ['x'] = "#fffdf8",
        ['X'] = "#ddd5c8",
        ['l'] = "#d6eef8",
        ['t'] = "#ffe680",
    };

    /// <summary>The eye pixels of the regular cat: two 2×3 eyes.</summary>
    private static readonly (int Rij, int Kolom)[] KatOgen = Blok(6, 6, 3, 2).Concat(Blok(6, 14, 3, 2)).ToArray();

    /// <summary>The kitten's eyes: 3×3, which is most of what makes it a kitten.</summary>
    private static readonly (int Rij, int Kolom)[] KittenOgen = Blok(6, 5, 3, 3).Concat(Blok(6, 14, 3, 3)).ToArray();

    /// <summary>The happy face's closed eyes: two little arches.</summary>
    private static readonly (int Rij, int Kolom)[] BlijeBoogjes =
    [
        (7, 6), (7, 7), (8, 5), (8, 8),
        (7, 14), (7, 15), (8, 13), (8, 16),
    ];

    /// <summary>The happy face's open mouth: a pink tongue under the "w".</summary>
    private static readonly Laag BlijeMond = new Laag(11, 10) { Rijen = ["pp", "oo"] };

    /// <summary>The majesty: lynx tufts on the ears and a fluffy cream ruff.</summary>
    private static readonly Laag[] MajesteitLagen =
    [
        new Laag(-2, 3) { Rijen = ["o", "o"] },
        new Laag(-2, 18) { Rijen = ["o", "o"] },
        new Laag(13, 5) { Rijen = ["cccccccccccc"] },
        new Laag(14, 4) { Rijen = ["cccccccccccccc"] },
        new Laag(15, 4) { Rijen = ["cc.cc.cc.cc.cc"] },
    ];

    /// <summary>Two little twinkles around the majesty, in their own group so they can sparkle.</summary>
    private static readonly Laag[] Glans =
    [
        new Laag(-5, 0) { Rijen = [".t.", "ttt", ".t."] },
        new Laag(1, 22) { Rijen = [".t.", "ttt", ".t."] },
    ];

    /// <summary>
    /// The wardrobe, as layers on the base sprite. A <c>~</c> is glass: it tints fur but leaves the
    /// eyes and outlines showing through.
    /// </summary>
    private static readonly Dictionary<Accessoire, Laag[]> Accessoires = new Dictionary<Accessoire, Laag[]>
    {
        [Accessoire.Feestmuts] =
        [
            new Laag(-6, 9) { Rijen = [".oo.", "orro", ".oo."] },
            new Laag(-3, 7)
            {
                Rijen =
                [
                    "..oyuo..",
                    "..ouyo..",
                    ".oyuyuo.",
                    ".ouyuyo.",
                    "oyuyuyuo",
                    "ouyuyuyo",
                    "oooooooo",
                ],
            },
        ],
        [Accessoire.Strikje] =
        [
            new Laag(13, 7)
            {
                Rijen =
                [
                    "oo....oo",
                    "orooooro",
                    "orrRRrro",
                    "orooooro",
                    "oo....oo",
                ],
            },
        ],
        [Accessoire.Bril] =
        [
            new Laag(5, 4)
            {
                Rijen =
                [
                    ".ooo.....ooo.",
                    "o~~~ooooo~~~o",
                    "o~~~o...o~~~o",
                    "o~~~o...o~~~o",
                    ".ooo.....ooo.",
                ],
            },
        ],
        [Accessoire.Bloem] =
        [
            new Laag(-1, 4)
            {
                Rijen =
                [
                    ".oo.oo.",
                    "oxxoxxo",
                    "oxxyxxo",
                    ".oyYyo.",
                    "oxxyxxo",
                    "oxxoxxo",
                    ".oo.oo.",
                ],
            },
        ],
        [Accessoire.Sjaal] =
        [
            new Laag(12, 2)
            {
                Rijen =
                [
                    ".oooooooooooooooo.",
                    "ogGggGggGggGggGggo",
                    "ogGggGggGggGggGggo",
                    ".ooooooooooogGgoo.",
                    "...........ogGgo..",
                    "...........ogGgo..",
                    "...........oxxxo..",
                    "...........ooooo..",
                ],
            },
        ],
        [Accessoire.Koksmuts] =
        [
            new Laag(-5, 6)
            {
                Rijen =
                [
                    "...oooo...",
                    ".ooxxxxoo.",
                    "oxxxxxxxxo",
                    "oxxXxxxXxo",
                    "oxxxxxxxxo",
                    ".oXxxxxXo.",
                    ".oxxxxxxo.",
                    ".oXXXXXXo.",
                    ".oxxxxxxo.",
                    ".oooooooo.",
                ],
            },
        ],
        [Accessoire.Kroon] =
        [
            new Laag(-2, 7)
            {
                Rijen =
                [
                    "y..yy..y",
                    "o..oo..o",
                    "oyoyyoyo",
                    "oyyrryyo",
                    "oYYYYYYo",
                    "oooooooo",
                ],
            },
        ],
    };

    /// <summary>The cat as markup that is safe to emit unencoded (it holds no input).</summary>
    /// <param name="houding">The pose.</param>
    /// <param name="vorm">The growth form.</param>
    /// <param name="accessoire">What it wears.</param>
    /// <returns>An inline SVG.</returns>
    public static IHtmlContent Html(KatHouding houding, KatVorm vorm = KatVorm.Kat, Accessoire accessoire = Accessoire.None)
        => new HtmlString(Svg(houding, vorm, accessoire));

    /// <summary>The cat as an SVG string.</summary>
    /// <param name="houding">The pose.</param>
    /// <param name="vorm">The growth form.</param>
    /// <param name="accessoire">What it wears.</param>
    /// <returns>An inline SVG.</returns>
    internal static string Svg(KatHouding houding, KatVorm vorm = KatVorm.Kat, Accessoire accessoire = Accessoire.None)
    {
        var ogen = vorm == KatVorm.Kitten ? KittenOgen : KatOgen;
        var pixels = Schilderen(houding, vorm, accessoire, ogen);
        var hoogte = Basis.Count;
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg class=\"kat {Klasse(houding)} {Klasse(vorm)}\" viewBox=\"0 {-Kop} {Breedte} {Kop + hoogte + 1}\" xmlns=\"http://www.w3.org/2000/svg\" shape-rendering=\"crispEdges\" aria-hidden=\"true\">");

        // A soft floor shadow under the paws, coloured by the theme.
        svg.Append(CultureInfo.InvariantCulture, $"<rect class=\"kat-schaduw\" x=\"3\" y=\"{hoogte}\" width=\"21\" height=\"1\"/>");
        svg.Append("<g class=\"kat-lijf\">");
        Rechthoeken(svg, pixels);

        if (houding == KatHouding.Zit)
        {
            // The blink: closed-eye pixels on top, shown for a moment by a CSS animation.
            var knipper = Leeg();
            Ogen(knipper, ogen, (rij, _) => rij == 8 ? 'o' : 'a');
            svg.Append("<g class=\"kat-knipper\">");
            Rechthoeken(svg, knipper);
            svg.Append("</g>");
        }

        svg.Append("</g>");

        if (vorm == KatVorm.Majesteit)
        {
            var glans = Leeg();
            Array.ForEach(Glans, laag => Leggen(glans, laag));
            svg.Append("<g class=\"kat-glans\">");
            Rechthoeken(svg, glans);
            svg.Append("</g>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>Paints the layers in order: base, form, the pose's face, then the accessory.</summary>
    private static char[][] Schilderen(KatHouding houding, KatVorm vorm, Accessoire accessoire, (int Rij, int Kolom)[] ogen)
    {
        var pixels = Leeg();
        Leggen(pixels, new Laag(0, 0) { Rijen = Basis });

        if (vorm == KatVorm.Kitten)
        {
            Ogen(pixels, KatOgen, (_, _) => 'a');
            Ogen(pixels, KittenOgen, (rij, kolom) => rij == 6 && (kolom == 5 || kolom == 14) ? 'w' : 'e');
        }
        else if (vorm == KatVorm.Majesteit)
        {
            Array.ForEach(MajesteitLagen, laag => Leggen(pixels, laag));
        }

        if (houding == KatHouding.Blij)
        {
            Ogen(pixels, ogen, (_, _) => 'a');
            Ogen(pixels, BlijeBoogjes, (_, _) => 'o');
            Leggen(pixels, BlijeMond);
        }
        else if (houding == KatHouding.Slaap)
        {
            Ogen(pixels, ogen, (rij, _) => rij == 8 ? 'o' : 'a');
        }

        // The accessory goes on last, so a hat sits on top of everything and glass tints whatever
        // face is underneath.
        if (Accessoires.TryGetValue(accessoire, out var lagen))
        {
            Array.ForEach(lagen, laag => Leggen(pixels, laag));
        }

        return pixels;
    }

    private static string Klasse(KatHouding houding) => houding switch
    {
        KatHouding.Blij => "kat-blij",
        KatHouding.Slaap => "kat-slaap",
        _ => "kat-zit",
    };

    private static string Klasse(KatVorm vorm) => vorm switch
    {
        KatVorm.Kitten => "kat-kitten",
        KatVorm.Majesteit => "kat-majesteit",
        _ => "kat-volwassen",
    };

    /// <summary>An empty canvas: the base sprite plus headroom, all transparent.</summary>
    private static char[][] Leeg()
        => [.. Enumerable.Range(0, Kop + Basis.Count).Select(_ => new string('.', Breedte).ToCharArray())];

    /// <summary>Paints a layer; <c>.</c> is transparent and <c>~</c> is glass over fur.</summary>
    private static void Leggen(char[][] pixels, Laag laag)
    {
        for (var r = 0; r < laag.Rijen.Count; r++)
        {
            for (var k = 0; k < laag.Rijen[r].Length; k++)
            {
                Verf(pixels, laag.Rij + r + Kop, laag.Kolom + k, laag.Rijen[r][k]);
            }
        }
    }

    private static void Verf(char[][] pixels, int y, int x, char teken)
    {
        if (teken == '.' || y < 0 || y >= pixels.Length || x < 0 || x >= Breedte)
        {
            return;
        }

        if (teken != '~')
        {
            pixels[y][x] = teken;
        }
        else if (pixels[y][x] is 'a' or 'b' or 'c' or 's' or 'k')
        {
            pixels[y][x] = 'l';
        }
    }

    private static void Ogen(char[][] pixels, (int Rij, int Kolom)[] cellen, Func<int, int, char> kleur)
    {
        foreach (var (rij, kolom) in cellen)
        {
            pixels[rij + Kop][kolom] = kleur(rij, kolom);
        }
    }

    private static IEnumerable<(int Rij, int Kolom)> Blok(int rij, int kolom, int hoogte, int breedte)
        => Enumerable.Range(rij, hoogte).SelectMany(r => Enumerable.Range(kolom, breedte).Select(k => (r, k)));

    /// <summary>One rect per horizontal run of equal colour, so the SVG stays small.</summary>
    private static void Rechthoeken(StringBuilder svg, char[][] pixels) => Pixels.Rechthoeken(svg, pixels, Kleuren, -Kop);

    /// <summary>A block of pixels to paint at a position on the base sprite.</summary>
    /// <param name="Rij">The row of its top-left corner (negative is headroom).</param>
    /// <param name="Kolom">The column of its top-left corner.</param>
    private sealed record Laag(int Rij, int Kolom)
    {
        /// <summary>Its pixels, one string per row.</summary>
        public IReadOnlyList<string> Rijen { get; init; } = [];
    }
}
