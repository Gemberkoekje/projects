using Vista.Astronomy;
using Vista.Composition;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>
/// The auto camera's composition scores (vista-plan §10 M1.14) on synthetic buffers: the near-field limit and the lens, the layers, the
/// framing mass, the leading line, the peak and the range's summits, the skyline and the sky, the sun, and the squint test and the values
/// by layer on synthetic images.
/// </summary>
[TestClass]
public sealed class CompositionScoreTests
{
    private const int W = 64, H = 36;

    private static readonly CameraSpec Level = new() { Position = new WorldPoint(0, 1000, 0), FocalLengthMm = 50.0 };

    /// <summary>Side light at golden hour: the sun 90° to the right of the camera's heading, 5° up.</summary>
    private static readonly SunPosition SideSun = new(90.0, 5.0, 5.0, 0.0, 0.0);

    private static FramingContext Context(CompositionIntent intent = CompositionIntent.Peak) =>
        new(Level, SideSun, intent, new WorldPoint(0, 3000, 50_000), 50_000);

    /// <summary>A buffer whose pixels <paramref name="pixel"/> describes (a null depth for the sky), every ground pixel 500 m above its floor and level.</summary>
    private static CompositionBuffer Buffer(Func<int, int, float?> depth, Func<int, int, GroundClass>? kind = null, int w = W, int h = H)
    {
        var buffer = new CompositionBuffer(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (depth(x, y) is not float d)
                    continue;
                int p = y * w + x;
                buffer.Depth[p] = d;
                buffer.Class[p] = kind?.Invoke(x, y) ?? GroundClass.Rock;
                buffer.Normal[p * 3 + 1] = 1f;
                buffer.Rise[p] = 500f;
            }
        }
        return buffer;
    }

    /// <summary>Sky above row 12, then three bands: a background range 20 km off, a middle ground at 3 km and a foreground at 400 m.</summary>
    private static float? Layered(int x, int y) => y < 12 ? null : y < 20 ? 20_000f : y < 28 ? 3_000f : 400f;

    private static CompositionScores Score(CompositionBuffer buffer, CompositionIntent intent = CompositionIntent.Peak) =>
        CompositionScorer.Score(buffer, Context(intent), CompositionLimits.Default);

    [TestMethod]
    public void NearField_BreaksTheLimit_UnlessItIsSnow()
    {
        float? Near(int x, int y) => y >= H - 3 ? 60f : Layered(x, y);
        CompositionScores rock = Score(Buffer(Near));
        Assert.AreEqual(3.0 / H, rock.NearShare, 1e-9);
        Assert.AreEqual("nearField", rock.Breaks(CompositionLimits.Default));
        CompositionScores snow = Score(Buffer(Near, (x, y) => y >= H - 3 ? GroundClass.Snow : GroundClass.Rock));
        Assert.AreEqual(0.0, snow.NearShare);
        Assert.IsNull(snow.Breaks(CompositionLimits.Default));
        Assert.IsNull(Score(Buffer(Layered)).Breaks(CompositionLimits.Default));
    }

    [TestMethod]
    public void GroundAtTheLens_BreaksTheLimit_SnowOrNot()
    {
        float? Blocked(int x, int y) => x < 3 && y >= H - 3 ? 10f : Layered(x, y);
        CompositionScores scores = Score(Buffer(Blocked, (_, _) => GroundClass.Snow));
        Assert.AreEqual(9.0 / (W * H), scores.LensShare, 1e-9);
        Assert.AreEqual("lens", scores.Breaks(CompositionLimits.Default));
    }

    [TestMethod]
    public void Triangles_OverTheMargin_BreakTheLimit()
    {
        var limits = CompositionLimits.Default;
        Assert.AreEqual(40_800_000L, limits.TriangleLimit);
        CompositionScores scores = Score(Buffer(Layered));
        Assert.IsNull((scores with { Triangles = 38_200_000 }).Breaks(limits));
        Assert.AreEqual("triangles", (scores with { Triangles = 45_000_000 }).Breaks(limits));
    }

    [TestMethod]
    public void Layers_ThreeRidges_ScoreAboveAContinuousSlope()
    {
        CompositionScores layered = Score(Buffer(Layered));
        Assert.IsTrue(layered.Layers > 0.9, $"three ridges: {layered.Layers}");
        // The same range of depths growing smoothly up the frame, no ridge standing in front of another.
        float? Slope(int x, int y) => y < 12 ? null : (float)(400.0 * Math.Pow(50.0, (H - 1 - y) / 23.0));
        CompositionScores slope = Score(Buffer(Slope));
        Assert.AreEqual(0.0, slope.Layers, 1e-9, "no silhouettes, no layers");
        // One ridge alone: half the silhouettes, and one group too small.
        float? Two(int x, int y) => y < 12 ? null : y < 24 ? 20_000f : 400f;
        Assert.IsTrue(Score(Buffer(Two)).Layers < layered.Layers);
    }

    [TestMethod]
    public void Framing_AMassAtOneEdge_NotCentredNorAtBoth()
    {
        float? Mass(int x, int y, Func<int, bool> inMass) => inMass(x) && y >= 10 ? 200f : Layered(x, y);
        double left = Score(Buffer((x, y) => Mass(x, y, c => c < 13))).Framing;
        double centred = Score(Buffer((x, y) => Mass(x, y, c => c is >= 26 and < 39))).Framing;
        double both = Score(Buffer((x, y) => Mass(x, y, c => c < 13 || c >= W - 13))).Framing;
        Assert.IsTrue(left > 0.8, $"at one edge: {left}");
        Assert.IsTrue(centred < 0.05, $"centred: {centred}");
        Assert.IsTrue(both < 0.1, $"at both edges: {both}");
    }

    [TestMethod]
    public void LeadingLine_ADiagonalBeatsALevelLine()
    {
        // Below a skyline at row 8, near ground under a line from the lower left corner up to the right (34° from the level), far above it.
        float? Diagonal(int x, int y) => y < 8 ? null : y > H - 1 - (x * 27.0 / 40.0) ? 500f : 5000f;
        float? LevelLine(int x, int y) => y < 8 ? null : y > 22 ? 500f : 5000f;
        double diagonal = Score(Buffer(Diagonal)).LeadingLine;
        double level = Score(Buffer(LevelLine)).LeadingLine;
        Assert.IsTrue(diagonal > 0.8, $"diagonal: {diagonal}");
        Assert.IsTrue(level < 0.05, $"level: {level}");
    }

    /// <summary>A skyline: <paramref name="row"/>(x) is the first ground row of column x; the ground below it 8 km off, snow just under it.</summary>
    private static CompositionBuffer Skyline(Func<int, int> row) =>
        Buffer((x, y) => y < row(x) ? null : 8000f, (x, y) => y < row(x) + 4 ? GroundClass.Snow : GroundClass.Rock);

    private static Func<int, int> Peak(int summitX, int summitY, int baseRow = 16) => x => Math.Min(baseRow, summitY + Math.Abs(x - summitX) / 2);

    [TestMethod]
    public void Peak_NearAThird_BeatsCentred_AndACutSummitScoresNothing()
    {
        CompositionScores third = Score(Skyline(Peak(21, 7)));
        CompositionScores centred = Score(Skyline(Peak(32, 7)));
        CompositionScores cut = Score(Skyline(Peak(21, 0)));
        Assert.AreEqual(21.5 / W, third.SummitX, 1e-9);
        Assert.AreEqual(7.0 / H, third.SummitY, 1e-9);
        Assert.IsTrue(third.Peak > 0.5, $"at a third: {third.Peak}");
        Assert.IsTrue(centred.Peak < 0.1 * third.Peak, $"centred: {centred.Peak}");
        Assert.AreEqual(0.0, cut.Peak, "a summit the top edge cuts");
        // A summit barely above a level skyline does not dominate it.
        Assert.IsTrue(Score(Skyline(Peak(21, 14))).Peak < third.Peak);
    }

    [TestMethod]
    public void Range_CountsItsSummits()
    {
        int[] four = [.. Enumerable.Range(0, W).Select(x => 10 + Math.Abs((x % 16) - 8) / 2)];
        Assert.AreEqual(4, CompositionScorer.Summits(four, H).Count);
        int[] one = [.. Enumerable.Range(0, W).Select(Peak(32, 7))];
        Assert.AreEqual(1, CompositionScorer.Summits(one, H).Count);
        // Bumps of a pixel are not summits.
        int[] rough = [.. Enumerable.Range(0, W).Select(x => 12 + (x % 2))];
        Assert.AreEqual(0, CompositionScorer.Summits(rough, H).Count, "a level skyline has no summit");
        double many = Score(Skyline(x => four[x]), CompositionIntent.Range).Peak;
        double single = Score(Skyline(Peak(32, 7)), CompositionIntent.Range).Peak;
        Assert.IsTrue(many > single, $"{many} against {single}");
    }

    [TestMethod]
    public void Skyline_NearTheUpperThird_AndTheSkysShare()
    {
        CompositionScores third = Score(Buffer(Layered));
        Assert.AreEqual(12.0 / H, third.SkylineRow, 1e-9);
        Assert.AreEqual(1.0, third.Horizon, 1e-9);
        Assert.AreEqual(1.0 / 3.0, third.SkyShare, 1e-9);
        Assert.IsTrue(third.Sky > 0.9);
        CompositionScores low = Score(Buffer((x, y) => y < 30 ? null : 1000f));
        Assert.IsTrue(low.Horizon < 0.01 && low.Sky < 0.01, "almost all sky");
    }

    [TestMethod]
    public void Sun_SideLightBest_FlatLightAndTheSunInFrameLeast_ItsHeightTempers()
    {
        double At(double azimuth, double elevation) => CompositionScorer.SunScore(Level, new SunPosition(azimuth, elevation, elevation, 0.0, 0.0), W, H);
        Assert.AreEqual(1.0, At(90.0, 5.0), 1e-9, "side light");
        Assert.AreEqual(1.0, At(270.0, 5.0), 1e-9, "side light from the left");
        Assert.AreEqual(0.4, At(180.0, 5.0), 1e-9, "flat light, the sun behind the camera");
        Assert.AreEqual(0.85, At(40.0, 5.0), 1e-9, "back light, the sun out of the frame");
        Assert.AreEqual(0.1, At(5.0, 5.0), 1e-9, "into the sun");
        Assert.AreEqual(1.0 - 0.25 * 0.6, At(180.0, 60.0), 1e-9, "a high sun's direction matters less");
    }

    /// <summary>The sun on the world's west (x &lt; 0) only, and only on ground facing it at all.</summary>
    private sealed class WestLit : ISunlight
    {
        public bool Lit(double x, double z, Vector3D normal, Vector3D toSun) => x < 0.0 && Vector3D.Dot(normal, toSun) > 0.0;
    }

    [TestMethod]
    public void Light_TheSubjectInTheSun_NullWithoutAWayToTell()
    {
        // The camera looks north, so the left of the frame is the world's west. The sun stands 5° up, so level ground faces it.
        CompositionScores lit = CompositionScorer.Score(Skyline(Peak(21, 7)), Context(), CompositionLimits.Default, new WestLit());
        CompositionScores shaded = CompositionScorer.Score(Skyline(Peak(45, 7)), Context(), CompositionLimits.Default, new WestLit());
        Assert.AreEqual(1.0, lit.Light!.Value, 1e-9, "the summit on the lit side");
        Assert.AreEqual(0.0, shaded.Light!.Value, 1e-9, "the summit on the shaded side");
        Assert.IsNull(Score(Skyline(Peak(21, 7))).Light, "no way to trace toward the sun");
        var night = Context() with { Sun = new SunPosition(90.0, -4.0, -4.0, 0.0, 0.0) };
        Assert.IsNull(CompositionScorer.Score(Skyline(Peak(21, 7)), night, CompositionLimits.Default, new WestLit()).Light, "the sun below the horizon");
        Assert.IsTrue(lit.Total(CompositionWeights.Default) > shaded.Total(CompositionWeights.Default) - 0.01);
    }

    [TestMethod]
    public void KMeans3_FindsThreeGroups_AndAnEvenSpreadSeparatesLess()
    {
        (double[] centres, int[] counts, double separation) = CompositionScorer.KMeans3([1.0, 1.1, 0.9, 5.0, 5.1, 4.9, 9.0, 9.2, 8.8]);
        Assert.AreEqual(1.0, centres[0], 1e-9);
        Assert.AreEqual(5.0, centres[1], 1e-9);
        Assert.AreEqual(9.0, centres[2], 1e-9);
        CollectionAssert.AreEqual(new[] { 3, 3, 3 }, counts);
        Assert.IsTrue(separation > 0.99);
        double even = CompositionScorer.KMeans3([.. Enumerable.Range(0, 3000).Select(i => i / 2999.0)]).Separation;
        Assert.AreEqual(8.0 / 9.0, even, 0.01);
    }

    [TestMethod]
    public void Squint_DistinctGroups_BeatAGradient_AndMud()
    {
        const int w = 192, h = 108;
        float[] Image(Func<int, int, float> value)
        {
            var values = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                    values[y * w + x] = value(x, y);
            }
            return values;
        }
        // A light sky, a mid-toned range, a dark foreground.
        double groups = ValueStructure.Squint(Image((_, y) => y < 36 ? 0.9f : y < 72 ? 0.5f : 0.1f), w, h).Score;
        double gradient = ValueStructure.Squint(Image((_, y) => 0.9f - 0.8f * y / (h - 1)), w, h).Score;
        // Busy mid-tones: noise around a mid grey, which the squint averages to mud.
        double mud = ValueStructure.Squint(Image((x, y) => 0.45f + 0.1f * ((x * 7 + y * 13) % 11) / 10f), w, h).Score;
        Assert.IsTrue(groups > 0.8, $"three groups: {groups}");
        Assert.IsTrue(gradient < 0.5 * groups, $"a gradient: {gradient}");
        Assert.IsTrue(mud < 0.05, $"mud: {mud}");
    }

    [TestMethod]
    public void Values_ALightBackground_ADarkForeground()
    {
        CompositionBuffer buffer = Buffer(Layered);
        float[] Values(float bg, float mg, float fg)
        {
            var values = new float[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                    values[y * W + x] = y < 12 ? 1f : y < 20 ? bg : y < 28 ? mg : fg;
            }
            return values;
        }
        (double score, double[] means) = ValueStructure.Layers(Values(0.8f, 0.5f, 0.2f), buffer);
        Assert.AreEqual(1.0, score, 1e-9);
        Assert.AreEqual(0.2, means[0], 1e-6, "the foreground first");
        Assert.AreEqual(0.0, ValueStructure.Layers(Values(0.2f, 0.5f, 0.8f), buffer).Score, 1e-9, "a dark background, a light foreground");
        Assert.AreEqual(0.5, ValueStructure.Layers(Values(0.8f, 0.5f, 0.5f), buffer).Score, 1e-9, "no step between the nearer two");
    }

    [TestMethod]
    public void Total_WeighsTheScoresThereAre()
    {
        var scores = new CompositionScores { Sky = 1, Horizon = 1, Peak = 1, Layers = 1, Framing = 1, LowerThird = 1, LeadingLine = 1, Sun = 1 };
        Assert.AreEqual(1.0, scores.Total(CompositionWeights.Default), 1e-12);
        CompositionWeights w = CompositionWeights.Default;
        double rendered = (scores with { Squint = 0.0, Values = 0.0 }).Total(w);
        double buffer = w.Sky + w.Horizon + w.Peak + w.Layers + w.Framing + w.LowerThird + w.LeadingLine + w.Sun;
        Assert.AreEqual(buffer / (buffer + w.Squint + w.Values), rendered, 1e-12, "a rendered finalist's squint test and values count too");
    }
}
