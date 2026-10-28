using Vista.Cli;
using Vista.Output;

namespace Vista.Tests;

/// <summary>
/// The golden suite's parts that need no GPU (vista-plan §10 M1.17): the EXR read back as written, the suite file, and the comparison's
/// verdicts: identical, within the tolerance (noise re-rolled), different (the image moved), missing.
/// </summary>
[TestClass]
public sealed class GoldenTests
{
    private static float[] Noisy(int width, int height, int seed, float brightness = 1f)
    {
        var random = new Random(seed);
        var rgb = new float[width * height * 3];
        for (int p = 0; p < width * height; p++)
        {
            float level = 0.05f + 0.2f * (p % width) / width;
            for (int c = 0; c < 3; c++)
                rgb[p * 3 + c] = brightness * level * (0.75f + 0.5f * (float)random.NextDouble()) * (1f + 0.2f * c);
        }
        return rgb;
    }

    [TestMethod]
    public void TheExr_ReadsBackAsWritten()
    {
        const int width = 37, height = 21;
        float[] rgb = Noisy(width, height, 1);
        foreach (ExrCompression compression in new[] { ExrCompression.None, ExrCompression.Zips, ExrCompression.Zip })
        {
            foreach (ExrPixelType type in new[] { ExrPixelType.Half, ExrPixelType.Float })
            {
                ExrImage image = ExrDecoder.Decode(ExrEncoder.EncodeRgb(rgb, width, height, type, compression));
                Assert.AreEqual((width, height), (image.Width, image.Height));
                CollectionAssert.AreEqual(new[] { "B", "G", "R" }, image.Channels.Select(c => c.Name).ToArray(), "sorted by name, as stored");
                float[] back = image.InterleavedRgb();
                for (int i = 0; i < rgb.Length; i++)
                {
                    float expected = type == ExrPixelType.Half ? (float)(Half)rgb[i] : rgb[i];
                    Assert.AreEqual(expected, back[i], $"{compression} {type} value {i}");
                }
            }
        }
        // A flat image compresses; a block stored raw (it would not shrink) reads back too.
        float[] flat = [.. Enumerable.Repeat(0.25f, width * height * 3)];
        CollectionAssert.AreEqual(flat, ExrDecoder.Decode(ExrEncoder.EncodeRgb(flat, width, height)).InterleavedRgb());
        Assert.ThrowsExactly<InvalidDataException>(() => ExrDecoder.Decode([1, 2, 3, 4, 5, 6, 7, 8]));
    }

    [TestMethod]
    public void TheSuite_ParsesAndResolvesItsFiles()
    {
        GoldenSuite suite = GoldenSuite.Parse("""
            {
              // a comment
              "schema": 1,
              "cases": [
                { "name": "sky", "checks": "the sky", "arguments": [ "--recipe", "../day.recipe.json", "--seed", "42", "--camera", "sky.camera.json", "--spp", "64" ] }
              ]
            }
            """);
        Assert.AreEqual(GoldenTolerance.Default, suite.Tolerance, "the default tolerance");
        string folder = Path.Combine(Path.GetTempPath(), "suite");
        IReadOnlyList<string> args = GoldenSuite.CommandLine(suite.Cases[0], folder, "out.png");
        CollectionAssert.AreEqual(new[]
        {
            "--preview", "--recipe", Path.GetFullPath(Path.Combine(folder, "..", "day.recipe.json")), "--seed", "42",
            "--camera", Path.GetFullPath(Path.Combine(folder, "sky.camera.json")), "--spp", "64", "--out", "out.png",
        }, args.ToArray());

        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => GoldenSuite.Parse("""{ "schema": 2, "cases": [ { "name": "a", "checks": "", "arguments": [] } ] }""")).Message, "schema 2");
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => GoldenSuite.Parse("""{ "schema": 1, "cases": [] }""")).Message, "no cases");
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => GoldenSuite.Parse(
            """{ "schema": 1, "cases": [ { "name": "a", "checks": "", "arguments": [] }, { "name": "A", "checks": "", "arguments": [] } ] }""")).Message, "named twice");
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => GoldenSuite.Parse(
            """{ "schema": 1, "cases": [ { "name": "a", "checks": "", "arguments": [ "--out", "x.png" ] } ] }""")).Message, "leave out --preview and --out");
    }

    [TestMethod]
    public void TheCommittedSuite_Parses()
    {
        // The suite and the views it names, in the checkout the tests were built from.
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !File.Exists(Path.Combine(folder, "vista-views", "goldens", "suite.json")))
            folder = Path.GetDirectoryName(folder);
        Assert.IsNotNull(folder, "the checkout's vista-views/goldens/suite.json");
        string path = Path.Combine(folder, "vista-views", "goldens", "suite.json");
        GoldenSuite suite = GoldenSuite.Parse(File.ReadAllText(path));
        foreach (GoldenCase golden in suite.Cases)
        {
            IReadOnlyList<string> args = GoldenSuite.CommandLine(golden, Path.GetDirectoryName(path)!, "x.png");
            Assert.IsTrue(PreviewCommandLine.TryParse([.. args], out _, out IReadOnlyList<string> errors), $"{golden.Name}: {string.Join(" ", errors)}");
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (args[i] is "--recipe" or "--camera")
                    Assert.IsTrue(File.Exists(args[i + 1]), $"{golden.Name}: {args[i + 1]}");
            }
        }
    }

    [TestMethod]
    public void TheComparison_TellsNoiseFromAMove()
    {
        const int width = 64, height = 48;
        GoldenTolerance tolerance = GoldenTolerance.Default;
        (PngImage Png, ExrImage Exr) Image(float[] rgb) =>
            (new PngImage(DisplayTransform.ToSrgb8(rgb, 1.0, DisplayCurve.Agx, DisplayLook.Punchy), width, height, PngColor.Rgb),
                ExrDecoder.Decode(ExrEncoder.EncodeRgb(rgb, width, height)));
        (PngImage png, ExrImage exr) = Image(Noisy(width, height, 1));

        GoldenResult same = GoldenComparison.Compare(png, exr, png, exr, tolerance);
        Assert.AreEqual(GoldenVerdict.Identical, same.Verdict);
        Assert.IsTrue(same.Passed);

        (PngImage rerolledPng, ExrImage rerolledExr) = Image(Noisy(width, height, 2));
        GoldenResult rerolled = GoldenComparison.Compare(rerolledPng, rerolledExr, png, exr, tolerance);
        Assert.AreEqual(GoldenVerdict.WithinTolerance, rerolled.Verdict, rerolled.Note);
        Assert.IsTrue(rerolled.PngChangedShare > 0.5, "the noise moved nearly every value");

        (PngImage brighterPng, ExrImage brighterExr) = Image(Noisy(width, height, 1, 1.05f));
        GoldenResult brighter = GoldenComparison.Compare(brighterPng, brighterExr, png, exr, tolerance);
        Assert.AreEqual(GoldenVerdict.Different, brighter.Verdict);
        Assert.AreEqual(0.05, brighter.MeanRelative, 0.002);
        StringAssert.Contains(brighter.Note, "mean luminance +5");

        // Warmer at the same luminance (a white balance's kind of change): the luminance passes, the PNG's red and blue do not.
        float[] warm = Noisy(width, height, 1);
        for (int p = 0; p < warm.Length; p += 3)
        {
            warm[p] *= 1.06f;
            warm[p + 2] *= 1f - 0.06f * 0.2126f / 0.0722f;
        }
        (PngImage warmPng, ExrImage warmExr) = Image(warm);
        GoldenResult warmer = GoldenComparison.Compare(warmPng, warmExr, png, exr, tolerance);
        Assert.AreEqual(GoldenVerdict.Different, warmer.Verdict);
        Assert.IsTrue(Math.Abs(warmer.MeanRelative) < tolerance.MeanRelative, $"the luminance kept: {warmer.MeanRelative}");
        StringAssert.Contains(warmer.Note, "PNG channel mean");
        Assert.AreEqual("±0.00", GoldenComparison.Signed(-0.001));
        Assert.AreEqual("-0.25", GoldenComparison.Signed(-0.25));

        var smaller = new PngImage(new byte[32 * 48 * 3], 32, 48, PngColor.Rgb);
        Assert.AreEqual(GoldenVerdict.Different, GoldenComparison.Compare(smaller, exr, png, exr, tolerance).Verdict);
        string missing = Path.Combine(Path.GetTempPath(), "no-such-golden");
        Assert.AreEqual(GoldenVerdict.Missing, GoldenComparison.Compare("a.png", "a.exr", missing + ".png", missing + ".exr", tolerance).Verdict);
    }
}
