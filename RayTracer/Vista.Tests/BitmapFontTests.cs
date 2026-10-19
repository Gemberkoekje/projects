using Vista.Output;

namespace Vista.Tests;

/// <summary>The built-in label font of the contact sheets (vista-plan §7.4: no font files).</summary>
[TestClass]
public sealed class BitmapFontTests
{
    [TestMethod]
    public void EveryPrintableAsciiCharacter_HasItsOwnGlyph()
    {
        for (char c = ' '; c <= '~'; c++)
            Assert.IsTrue(BitmapFont.Has(c), $"'{c}'");
        foreach (char c in "°×·−…")
            Assert.IsTrue(BitmapFont.Has(c), $"'{c}'");
        Assert.IsFalse(BitmapFont.Has('€'));
    }

    [TestMethod]
    public void Glyphs_AreDistinctAndOnlyTheSpaceIsBlank()
    {
        var seen = new Dictionary<string, char>();
        foreach (char c in BitmapFont.Characters)
        {
            bool[,] glyph = BitmapFont.Glyph(c);
            string key = string.Concat(glyph.Cast<bool>().Select(b => b ? '#' : '.'));
            Assert.AreEqual(c == ' ', !key.Contains('#'), $"'{c}'");
            // The minus sign deliberately shares the hyphen's glyph.
            if (c != '−' && c != '-')
                Assert.IsTrue(seen.TryAdd(key, c), $"'{c}' looks like '{seen.GetValueOrDefault(key)}'");
        }
    }

    [TestMethod]
    public void Descenders_ReachBelowTheBaseline()
    {
        foreach (char c in "gjpqy,;")
            Assert.IsTrue(Enumerable.Range(0, BitmapFont.GlyphWidth).Any(col => BitmapFont.Glyph(c)[7, col]), $"'{c}'");
        foreach (char c in "AZaz09")
            Assert.IsFalse(Enumerable.Range(0, BitmapFont.GlyphWidth).Any(col => BitmapFont.Glyph(c)[7, col]), $"'{c}'");
    }

    [TestMethod]
    public void Measure_CountsAdvancesWithoutTheTrailingGap()
    {
        Assert.AreEqual(0, BitmapFont.Measure("", 1));
        Assert.AreEqual(5, BitmapFont.Measure("a", 1));
        Assert.AreEqual(17, BitmapFont.Measure("abc", 1));
        Assert.AreEqual(34, BitmapFont.Measure("abc", 2));
    }

    [TestMethod]
    public void Fit_CutsWithAnEllipsisOnlyWhenNeeded()
    {
        Assert.AreEqual("seed=42", BitmapFont.Fit("seed=42", BitmapFont.Measure("seed=42", 1), 1));
        string cut = BitmapFont.Fit("time=goldenHour", 47, 1); // room for 8 characters
        Assert.AreEqual("time=go…", cut);
        Assert.IsTrue(BitmapFont.Measure(cut, 1) <= 47);
        Assert.AreEqual("time=go…", BitmapFont.Fit("time=goldenHour", 94, 2));
        Assert.AreEqual(string.Empty, BitmapFont.Fit("abc", 3, 1));
    }

    [TestMethod]
    public void Wrap_BreaksAtTheSeparator()
    {
        // 23 characters fit in 137 px at scale 1.
        IReadOnlyList<string> lines = BitmapFont.Wrap("alpine range · seed 42 · 320×180 · 64 spp", 137, 1);
        CollectionAssert.AreEqual(new[] { "alpine range · seed 42", "320×180 · 64 spp" }, lines.ToArray());
        CollectionAssert.AreEqual(new[] { "" }, BitmapFont.Wrap("", 100, 1).ToArray());
        Assert.AreEqual("averyveryverylongwo…", BitmapFont.Wrap("averyveryverylongword · x", 119, 1)[0]);
    }

    [TestMethod]
    public void Draw_PaintsScaledGlyphPixels()
    {
        const int width = 20, height = 22;
        var rgb = new byte[width * height * 3];
        BitmapFont.Draw(rgb, width, 1, 2, "T", 2, (200, 100, 50));

        // 'T' at scale 2: its top bar covers rows 2–3, columns 1–10; its stem is columns 5–6 down to row 15.
        Assert.AreEqual((200, 100, 50), Pixel(rgb, width, 1, 2));
        Assert.AreEqual((200, 100, 50), Pixel(rgb, width, 10, 3));
        Assert.AreEqual((200, 100, 50), Pixel(rgb, width, 6, 15));
        Assert.AreEqual((0, 0, 0), Pixel(rgb, width, 1, 4), "under the bar, beside the stem");
        Assert.AreEqual((0, 0, 0), Pixel(rgb, width, 6, 16), "below the stem");
        Assert.AreEqual((0, 0, 0), Pixel(rgb, width, 0, 2), "left of the glyph");
        int inked = Enumerable.Range(0, width * height).Count(p => rgb[p * 3] != 0);
        Assert.AreEqual(4 * (5 + 6), inked, "11 glyph pixels, each 2×2");
    }

    [TestMethod]
    public void Draw_ClipsAtTheImageEdgesAndBoxesUnknownCharacters()
    {
        const int width = 8, height = 8;
        var rgb = new byte[width * height * 3];
        BitmapFont.Draw(rgb, width, -3, -3, "€€€", 1, (255, 255, 255)); // must not throw
        Assert.AreEqual((255, 255, 255), Pixel(rgb, width, 1, 0), "the box's right edge after clipping");
        Assert.AreEqual((255, 255, 255), Pixel(rgb, width, 3, 3), "the next box's corner");
    }

    private static (int, int, int) Pixel(byte[] rgb, int width, int x, int y)
    {
        int o = (y * width + x) * 3;
        return (rgb[o], rgb[o + 1], rgb[o + 2]);
    }
}
