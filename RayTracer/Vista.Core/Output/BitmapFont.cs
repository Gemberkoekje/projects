namespace Vista.Output;

/// <summary>
/// A small built-in bitmap font for contact-sheet labels (vista-plan §7.4): procedural purity (§9) rules out
/// a font file, and labels only need to be legible. Glyphs are 5×7 pixels on a 9-row cell (two rows below the
/// baseline for descenders), drawn at an integer scale; the advance is 6 pixels per character at scale 1.
/// Printable ASCII plus <c>°</c>, <c>×</c>, <c>·</c>, <c>−</c> and <c>…</c>; anything else draws as a hollow
/// box, so a missing glyph shows instead of vanishing.
/// </summary>
public static class BitmapFont
{
    /// <summary>Glyph width in pixels at scale 1.</summary>
    public const int GlyphWidth = 5;

    /// <summary>Glyph cell height in pixels at scale 1 (7 above the baseline, 2 below).</summary>
    public const int GlyphHeight = 9;

    /// <summary>Horizontal distance from one character to the next at scale 1.</summary>
    public const int Advance = GlyphWidth + 1;

    /// <summary>Vertical distance from one line to the next at scale 1.</summary>
    public const int LineHeight = GlyphHeight + 2;

    /// <summary>What a too-long line ends with when it is cut to fit.</summary>
    public const string Ellipsis = "…";

    // One line per glyph: rows top to bottom separated by '|', '#' = ink. Rows 0–6 sit on the baseline; rows 7–8
    // are descenders and may be left out.
    private static readonly Dictionary<char, string> Art = new()
    {
        [' '] = "",
        ['!'] = "..#..|..#..|..#..|..#..|..#..|.....|..#..",
        ['"'] = ".#.#.|.#.#.|.#.#.",
        ['#'] = ".#.#.|.#.#.|#####|.#.#.|#####|.#.#.|.#.#.",
        ['$'] = "..#..|.####|#.#..|.###.|..#.#|####.|..#..",
        ['%'] = "##...|##..#|...#.|..#..|.#...|#..##|...##",
        ['&'] = ".##..|#..#.|#.#..|.#...|#.#.#|#..#.|.##.#",
        ['\''] = "..#..|..#..|..#..",
        ['('] = "...#.|..#..|.#...|.#...|.#...|..#..|...#.",
        [')'] = ".#...|..#..|...#.|...#.|...#.|..#..|.#...",
        ['*'] = ".....|..#..|#.#.#|.###.|#.#.#|..#..",
        ['+'] = ".....|..#..|..#..|#####|..#..|..#..",
        [','] = ".....|.....|.....|.....|.....|..#..|..#..|.#...",
        ['-'] = ".....|.....|.....|#####",
        ['.'] = ".....|.....|.....|.....|.....|.##..|.##..",
        ['/'] = ".....|....#|...#.|..#..|.#...|#....",
        ['0'] = ".###.|#...#|#..##|#.#.#|##..#|#...#|.###.",
        ['1'] = "..#..|.##..|..#..|..#..|..#..|..#..|.###.",
        ['2'] = ".###.|#...#|....#|...#.|..#..|.#...|#####",
        ['3'] = "#####|...#.|..#..|...#.|....#|#...#|.###.",
        ['4'] = "...#.|..##.|.#.#.|#..#.|#####|...#.|...#.",
        ['5'] = "#####|#....|####.|....#|....#|#...#|.###.",
        ['6'] = "..##.|.#...|#....|####.|#...#|#...#|.###.",
        ['7'] = "#####|....#|...#.|..#..|.#...|.#...|.#...",
        ['8'] = ".###.|#...#|#...#|.###.|#...#|#...#|.###.",
        ['9'] = ".###.|#...#|#...#|.####|....#|...#.|.##..",
        [':'] = ".....|.##..|.##..|.....|.##..|.##..",
        [';'] = ".....|.##..|.##..|.....|.##..|.##..|..#..|.#...",
        ['<'] = "...#.|..#..|.#...|#....|.#...|..#..|...#.",
        ['='] = ".....|.....|#####|.....|#####",
        ['>'] = ".#...|..#..|...#.|....#|...#.|..#..|.#...",
        ['?'] = ".###.|#...#|....#|...#.|..#..|.....|..#..",
        ['@'] = ".###.|#...#|....#|.##.#|#.#.#|#.#.#|.###.",
        ['A'] = ".###.|#...#|#...#|#...#|#####|#...#|#...#",
        ['B'] = "####.|#...#|#...#|####.|#...#|#...#|####.",
        ['C'] = ".###.|#...#|#....|#....|#....|#...#|.###.",
        ['D'] = "###..|#..#.|#...#|#...#|#...#|#..#.|###..",
        ['E'] = "#####|#....|#....|####.|#....|#....|#####",
        ['F'] = "#####|#....|#....|####.|#....|#....|#....",
        ['G'] = ".###.|#...#|#....|#.###|#...#|#...#|.####",
        ['H'] = "#...#|#...#|#...#|#####|#...#|#...#|#...#",
        ['I'] = ".###.|..#..|..#..|..#..|..#..|..#..|.###.",
        ['J'] = "..###|...#.|...#.|...#.|...#.|#..#.|.##..",
        ['K'] = "#...#|#..#.|#.#..|##...|#.#..|#..#.|#...#",
        ['L'] = "#....|#....|#....|#....|#....|#....|#####",
        ['M'] = "#...#|##.##|#.#.#|#.#.#|#...#|#...#|#...#",
        ['N'] = "#...#|#...#|##..#|#.#.#|#..##|#...#|#...#",
        ['O'] = ".###.|#...#|#...#|#...#|#...#|#...#|.###.",
        ['P'] = "####.|#...#|#...#|####.|#....|#....|#....",
        ['Q'] = ".###.|#...#|#...#|#...#|#.#.#|#..#.|.##.#",
        ['R'] = "####.|#...#|#...#|####.|#.#..|#..#.|#...#",
        ['S'] = ".####|#....|#....|.###.|....#|....#|####.",
        ['T'] = "#####|..#..|..#..|..#..|..#..|..#..|..#..",
        ['U'] = "#...#|#...#|#...#|#...#|#...#|#...#|.###.",
        ['V'] = "#...#|#...#|#...#|#...#|#...#|.#.#.|..#..",
        ['W'] = "#...#|#...#|#...#|#.#.#|#.#.#|#.#.#|.#.#.",
        ['X'] = "#...#|#...#|.#.#.|..#..|.#.#.|#...#|#...#",
        ['Y'] = "#...#|#...#|.#.#.|..#..|..#..|..#..|..#..",
        ['Z'] = "#####|....#|...#.|..#..|.#...|#....|#####",
        ['['] = ".###.|.#...|.#...|.#...|.#...|.#...|.###.",
        ['\\'] = ".....|#....|.#...|..#..|...#.|....#",
        [']'] = ".###.|...#.|...#.|...#.|...#.|...#.|.###.",
        ['^'] = "..#..|.#.#.|#...#",
        ['_'] = ".....|.....|.....|.....|.....|.....|.....|#####",
        ['`'] = ".#...|..#..|...#.",
        ['a'] = ".....|.....|.###.|....#|.####|#...#|.####",
        ['b'] = "#....|#....|#.##.|##..#|#...#|#...#|####.",
        ['c'] = ".....|.....|.###.|#....|#....|#...#|.###.",
        ['d'] = "....#|....#|.##.#|#..##|#...#|#...#|.####",
        ['e'] = ".....|.....|.###.|#...#|#####|#....|.###.",
        ['f'] = "..##.|.#..#|.#...|###..|.#...|.#...|.#...",
        ['g'] = ".....|.....|.####|#...#|#...#|#...#|.####|....#|.###.",
        ['h'] = "#....|#....|#.##.|##..#|#...#|#...#|#...#",
        ['i'] = "..#..|.....|.##..|..#..|..#..|..#..|.###.",
        ['j'] = "...#.|.....|..##.|...#.|...#.|...#.|...#.|#..#.|.##..",
        ['k'] = "#....|#....|#..#.|#.#..|##...|#.#..|#..#.",
        ['l'] = ".##..|..#..|..#..|..#..|..#..|..#..|.###.",
        ['m'] = ".....|.....|##.#.|#.#.#|#.#.#|#...#|#...#",
        ['n'] = ".....|.....|#.##.|##..#|#...#|#...#|#...#",
        ['o'] = ".....|.....|.###.|#...#|#...#|#...#|.###.",
        ['p'] = ".....|.....|####.|#...#|#...#|#...#|####.|#....|#....",
        ['q'] = ".....|.....|.####|#...#|#...#|#...#|.####|....#|....#",
        ['r'] = ".....|.....|#.##.|##..#|#....|#....|#....",
        ['s'] = ".....|.....|.####|#....|.###.|....#|####.",
        ['t'] = ".#...|.#...|###..|.#...|.#...|.#..#|..##.",
        ['u'] = ".....|.....|#...#|#...#|#...#|#..##|.##.#",
        ['v'] = ".....|.....|#...#|#...#|#...#|.#.#.|..#..",
        ['w'] = ".....|.....|#...#|#...#|#.#.#|#.#.#|.#.#.",
        ['x'] = ".....|.....|#...#|.#.#.|..#..|.#.#.|#...#",
        ['y'] = ".....|.....|#...#|#...#|#...#|#...#|.####|....#|.###.",
        ['z'] = ".....|.....|#####|...#.|..#..|.#...|#####",
        ['{'] = "...##|..#..|..#..|##...|..#..|..#..|...##",
        ['|'] = "..#..|..#..|..#..|..#..|..#..|..#..|..#..",
        ['}'] = "##...|..#..|..#..|...##|..#..|..#..|##...",
        ['~'] = ".....|.....|.#...|#.#.#|...#.",
        ['°'] = ".##..|#..#.|.##..",
        ['×'] = ".....|#...#|.#.#.|..#..|.#.#.|#...#",
        ['·'] = ".....|.....|.....|..#..",
        ['−'] = ".....|.....|.....|#####",
        ['…'] = ".....|.....|.....|.....|.....|.....|#.#.#",
    };

    private const string MissingArt = "#####|#...#|#...#|#...#|#...#|#...#|#####";

    private static readonly Dictionary<char, bool[,]> Glyphs = Art.ToDictionary(kv => kv.Key, kv => Parse(kv.Key, kv.Value));
    private static readonly bool[,] Missing = Parse('?', MissingArt);

    /// <summary>Every character with its own glyph.</summary>
    public static IReadOnlyCollection<char> Characters => Glyphs.Keys;

    /// <summary>True when <paramref name="c"/> has its own glyph (otherwise it draws as a hollow box).</summary>
    public static bool Has(char c) => Glyphs.ContainsKey(c);

    /// <summary>The glyph of <paramref name="c"/> as ink flags, [row, column] on the 9×5 cell.</summary>
    public static bool[,] Glyph(char c) => (bool[,])(Glyphs.GetValueOrDefault(c) ?? Missing).Clone();

    /// <summary>Width in pixels of <paramref name="text"/> on one line at <paramref name="scale"/> (no trailing gap).</summary>
    public static int Measure(string text, int scale)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        return text.Length == 0 ? 0 : (text.Length * Advance - 1) * scale;
    }

    /// <summary>
    /// <paramref name="text"/> cut to fit <paramref name="maxWidth"/> pixels at <paramref name="scale"/>,
    /// ending in <see cref="Ellipsis"/> when anything was cut.
    /// </summary>
    public static string Fit(string text, int maxWidth, int scale)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Measure(text, scale) <= maxWidth)
            return text;
        int chars = Math.Max(0, (maxWidth / scale + 1) / Advance - Ellipsis.Length);
        return chars == 0 ? string.Empty : text[..chars] + Ellipsis;
    }

    /// <summary>
    /// Breaks <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/> at
    /// <paramref name="scale"/>, splitting only after <paramref name="separator"/>; a piece that is still too
    /// long is cut with <see cref="Fit"/>.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, int maxWidth, int scale, string separator = " · ")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(separator);
        var lines = new List<string>();
        string current = string.Empty;
        foreach (string piece in text.Split(separator))
        {
            string candidate = current.Length == 0 ? piece : current + separator + piece;
            if (current.Length > 0 && Measure(candidate, scale) > maxWidth)
            {
                lines.Add(Fit(current, maxWidth, scale));
                current = piece;
            }
            else
            {
                current = candidate;
            }
        }
        lines.Add(Fit(current, maxWidth, scale));
        return lines;
    }

    /// <summary>
    /// Draws <paramref name="text"/> into a tightly packed 8-bit RGB image <paramref name="width"/> pixels wide,
    /// the glyph cell's top-left corner at (<paramref name="x"/>, <paramref name="y"/>). Pixels outside the
    /// image are skipped.
    /// </summary>
    public static void Draw(Span<byte> rgb, int width, int x, int y, string text, int scale, (byte R, byte G, byte B) color)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        int height = rgb.Length / (width * 3);
        for (int k = 0; k < text.Length; k++)
        {
            bool[,] glyph = Glyphs.GetValueOrDefault(text[k]) ?? Missing;
            int left = x + k * Advance * scale;
            for (int row = 0; row < GlyphHeight; row++)
            {
                for (int col = 0; col < GlyphWidth; col++)
                {
                    if (!glyph[row, col])
                        continue;
                    for (int dy = 0; dy < scale; dy++)
                    {
                        int py = y + row * scale + dy;
                        if (py < 0 || py >= height)
                            continue;
                        for (int dx = 0; dx < scale; dx++)
                        {
                            int px = left + col * scale + dx;
                            if (px < 0 || px >= width)
                                continue;
                            int o = (py * width + px) * 3;
                            rgb[o] = color.R;
                            rgb[o + 1] = color.G;
                            rgb[o + 2] = color.B;
                        }
                    }
                }
            }
        }
    }

    private static bool[,] Parse(char c, string art)
    {
        var glyph = new bool[GlyphHeight, GlyphWidth];
        if (art.Length == 0)
            return glyph;
        string[] rows = art.Split('|');
        if (rows.Length > GlyphHeight || rows.Any(r => r.Length != GlyphWidth || r.Any(ch => ch is not ('#' or '.'))))
            throw new InvalidOperationException($"Glyph '{c}' is malformed.");
        for (int row = 0; row < rows.Length; row++)
        {
            for (int col = 0; col < GlyphWidth; col++)
                glyph[row, col] = rows[row][col] == '#';
        }
        return glyph;
    }
}
