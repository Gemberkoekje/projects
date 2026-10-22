using System.Numerics;
using Vista.Output;

namespace Vista.Tests;

/// <summary>
/// The PNG's display curves (vista-plan §10 M1.10a), each against values from the implementation it is ported from: AgX against
/// three.js r169's own <c>AgXToneMapping</c>, its punchy look (M1.11b) against the same with Filament's look added, ACES against
/// Narkowicz's formula.
/// </summary>
[TestClass]
public sealed class DisplayCurveTests
{
    /// <summary>
    /// three.js r169's <c>AgXToneMapping</c> (<c>src/renderers/shaders/ShaderChunk/tonemapping_pars_fragment.glsl.js</c>, loaded from
    /// unpkg.com/three@0.169.0 on 2026-10-21) compiled as it is by WebGL 2 in a browser, run on these linear Rec.709 inputs with
    /// <c>toneMappingExposure</c> 1 into a 32-bit float target and read back: (input, output), 9 significant digits.
    /// </summary>
    private static readonly (Vector3 In, Vector3 Out)[] ThreeJs =
    [
        (new(0f, 0f, 0f), new(0f, 0f, 0f)),
        (new(0.0001f, 0.0001f, 0.0001f), new(0f, 0f, 0f)),
        (new(0.001f, 0.001f, 0.001f), new(0.000105759231f, 0.000105725230f, 0.000105731859f)),
        (new(0.01f, 0.01f, 0.01f), new(0.00599623937f, 0.00599414436f, 0.00599463983f)),
        (new(0.05f, 0.05f, 0.05f), new(0.0613825396f, 0.0613660403f, 0.0613674261f)),
        (new(0.18f, 0.18f, 0.18f), new(0.214548990f, 0.214502335f, 0.214499235f)),
        (new(0.5f, 0.5f, 0.5f), new(0.425196886f, 0.425119251f, 0.425102383f)),
        (new(1f, 1f, 1f), new(0.590227306f, 0.590135276f, 0.590101421f)),
        (new(2f, 2f, 2f), new(0.743398607f, 0.743291318f, 0.743236363f)),
        (new(4f, 4f, 4f), new(0.861013293f, 0.860911191f, 0.860836506f)),
        (new(8f, 8f, 8f), new(0.936872005f, 0.936780512f, 0.936689973f)),
        (new(16f, 16f, 16f), new(0.995212197f, 0.995096445f, 0.995013177f)),
        (new(100f, 100f, 100f), new(0.996977806f, 0.996878028f, 0.996778548f)),
        (new(1f, 0f, 0f), new(0.723274231f, 0.105847552f, 0.0667358190f)),
        (new(0f, 1f, 0f), new(0.234997213f, 0.584540486f, 0.130355686f)),
        (new(0f, 0f, 1f), new(0.0340126306f, 0.135471195f, 0.730009317f)),
        (new(0.18f, 0.09f, 0.03f), new(0.217246145f, 0.112558588f, 0.0432382524f)),
        (new(0.4f, 0.25f, 0.12f), new(0.378737509f, 0.269383430f, 0.168395773f)),
        (new(1.5f, 0.9f, 0.4f), new(0.697139382f, 0.559627652f, 0.412848622f)),
        (new(4f, 2.5f, 1.2f), new(0.873681486f, 0.782227814f, 0.669101417f)),
        (new(8f, 4f, 1f), new(0.958867610f, 0.860240161f, 0.709223747f)),
        (new(0.02f, 0.05f, 0.12f), new(0.0204572249f, 0.0706528798f, 0.160067722f)),
        (new(0.3f, 0.6f, 1.4f), new(0.354716629f, 0.498416871f, 0.696365535f)),
        (new(0.9f, 0.05f, 0.4f), new(0.632144094f, 0.179697156f, 0.382047027f)),
        (new(0.001f, 0.2f, 0.003f), new(0.0268874224f, 0.220347241f, 0.0110861966f)),
        (new(-0.1f, 0.5f, 0.2f), new(0.0234726612f, 0.428286821f, 0.244051129f)),
    ];

    /// <summary>
    /// Three.js r169's <c>AgXToneMapping</c> as in <see cref="ThreeJs"/> (loaded from unpkg.com/three@0.169.0 on 2026-10-21, and checked
    /// to give those values again), with Filament's <c>agxLook</c> for the punchy look (<c>filament/src/ToneMapper.cpp</c>, main on
    /// 2026-10-21) written into it in GLSL after the sigmoid, as Filament applies it, with M1.11b's one change (the channel clamped at 0
    /// before the power): the same inputs, run the same way.
    /// </summary>
    private static readonly Vector3[] ThreeJsPunchy =
    [
        new(2.13132253e-7f, 2.13110937e-7f, 2.13089635e-7f),
        new(2.13132253e-7f, 2.13110937e-7f, 2.13089635e-7f),
        new(0f, 0f, 0f),
        new(0.000215106134f, 0.000214881133f, 0.0002150082f),
        new(0.0132472627f, 0.0132404733f, 0.0132431183f),
        new(0.0966401994f, 0.0966064557f, 0.0966143757f),
        new(0.276182413f, 0.276109248f, 0.27611506f),
        new(0.453755856f, 0.453661025f, 0.453653395f),
        new(0.641936421f, 0.641817331f, 0.641785502f),
        new(0.79981941f, 0.79971087f, 0.799650967f),
        new(0.907304943f, 0.907217562f, 0.907132626f),
        new(0.992829859f, 0.992699981f, 0.992631435f),
        new(0.99545145f, 0.995351791f, 0.995252371f),
        new(0.739496231f, 0f, 0f),
        new(0.0189362038f, 0.485038161f, 0f),
        new(0f, 0.0408605859f, 0.895308554f),
        new(0.117035799f, 0.0308768302f, 0f),
        new(0.256313503f, 0.132623747f, 0.0412926599f),
        new(0.62161839f, 0.412114024f, 0.215364799f),
        new(0.846637309f, 0.688639641f, 0.504810929f),
        new(0.97212702f, 0.795037448f, 0.541567564f),
        new(0f, 0.017166473f, 0.0884260088f),
        new(0.163701683f, 0.356173247f, 0.664369106f),
        new(0.592254162f, 0.0372190811f, 0.263510108f),
        new(0f, 0.115833431f, 0f),
        new(0f, 0.30763039f, 0.0977621228f),
    ];

    /// <summary>True when Filament's look clipped the colour: the clamp left a channel of the reference at 0.</summary>
    private static bool Clipped(Vector3 reference) => Math.Min(reference.X, Math.Min(reference.Y, reference.Z)) == 0f;

    [TestMethod]
    public void AgxPunchy_MatchesThreeJsWithFilamentsLook_WhereItDidNotClip()
    {
        // The look's power and saturation scale the sigmoid's float cancellation (see Agx_MatchesThreeJs) a little. Since M1.11c a
        // colour Filament's look clipped gets less saturation (AgxPunchy_HoldsTheSaturationBack_JustEnough); every other colour, and
        // every grey, keeps Filament's look and its saturation of 1.4 exactly.
        float worst = 0f;
        string where = string.Empty;
        int kept = 0;
        for (int i = 0; i < ThreeJs.Length; i++)
        {
            Vector3 input = ThreeJs[i].In;
            bool grey = input.X == input.Y && input.Y == input.Z;
            if (Clipped(ThreeJsPunchy[i]) && !grey)
                continue;
            kept++;
            Vector3 difference = Vector3.Abs(DisplayTransform.Agx(input, DisplayLook.Punchy) - ThreeJsPunchy[i]);
            float largest = Math.Max(difference.X, Math.Max(difference.Y, difference.Z));
            if (largest > worst)
                (worst, where) = (largest, input.ToString());
            if (!Clipped(ThreeJsPunchy[i]))
            {
                DisplayTransform.AgxLinear(input, DisplayLook.Punchy, out float saturation);
                Assert.AreEqual(DisplayTransform.PunchySaturation, saturation, $"{input}: Filament's saturation");
            }
        }
        Assert.AreEqual(19, kept, "the 13 greys and the 6 colours Filament's look kept whole");
        Assert.IsTrue(worst <= 3e-5f, $"worst {worst:E2} at {where}");
    }

    [TestMethod]
    public void AgxPunchy_HoldsTheSaturationBack_JustEnough()
    {
        // Colours over 18 stops and every hue, saturated to the edge of Rec.709 and beyond (a negative channel). Under the punchy
        // look no channel of the PNG's linear Rec.709 goes below 0, unless even the power alone (saturation 1) takes it there; a
        // colour held back is held back just enough: its lowest channel sits at 0, within the bisection's step.
        int heldBack = 0, floor = 0;
        foreach (float level in new[] { 1e-3f, 0.01f, 0.05f, 0.18f, 0.7f, 3f, 20f, 200f })
        {
            for (int h = 0; h < 24; h++)
            {
                foreach (float chroma in new[] { 0.3f, 0.7f, 0.95f, 1.2f })
                {
                    double angle = h * Math.PI / 12.0;
                    var direction = new Vector3((float)Math.Cos(angle), (float)Math.Cos(angle - 2.0 * Math.PI / 3.0), (float)Math.Cos(angle + 2.0 * Math.PI / 3.0));
                    Vector3 input = level * (Vector3.One + chroma * direction);
                    Vector3 linear = DisplayTransform.AgxLinear(input, DisplayLook.Punchy, out float saturation);
                    float lowest = Math.Min(linear.X, Math.Min(linear.Y, linear.Z));
                    Assert.IsTrue(saturation >= 1f && saturation <= DisplayTransform.PunchySaturation, $"{input}: saturation {saturation}");
                    if (saturation > 1f)
                        Assert.IsTrue(lowest >= 0f, $"{input}: {linear} at saturation {saturation}");
                    if (saturation < DisplayTransform.PunchySaturation)
                    {
                        Assert.IsTrue(lowest <= 1e-4f, $"{input}: held back to {saturation}, its lowest channel {lowest} could have had more");
                        if (saturation > 1f)
                            heldBack++;
                        else
                            floor++;
                    }
                    Assert.AreEqual(Vector3.Clamp(linear, Vector3.Zero, Vector3.One), DisplayTransform.Agx(input, DisplayLook.Punchy));
                }
            }
        }
        Assert.IsTrue(heldBack > 100, $"{heldBack} colours held back");
        Assert.IsTrue(floor > 0, "and a few the power alone clips");
    }

    [TestMethod]
    public void AgxPunchy_HeldBack_KeepsASkysHueWithinAFewDegreesOfTheBase()
    {
        // Clear-sky blues as the 3070 renders them (the sky view's pixels after its exposure, M1.11c's log) and deeper ones: every one
        // clips under Filament's saturation, and held back its Oklab hue stays within a few degrees of AgX's base, the size of the
        // look's own shift on a blue it does not clip.
        Vector3[] skies =
        [
            new(0.0295f, 0.0556f, 0.1206f), new(0.0315f, 0.0586f, 0.1312f), new(0.0419f, 0.0866f, 0.1812f), new(0.0371f, 0.0655f, 0.1440f),
            new(0.02f, 0.05f, 0.12f), new(0.015f, 0.04f, 0.13f), new(0.05f, 0.12f, 0.35f),
        ];
        foreach (Vector3 sky in skies)
        {
            DisplayTransform.AgxLinear(sky, DisplayLook.Punchy, out float saturation);
            Assert.IsTrue(saturation < DisplayTransform.PunchySaturation, $"{sky} clips under Filament's saturation");
            double shift = HueDistance(DisplayTransform.Agx(sky, DisplayLook.Punchy), DisplayTransform.Agx(sky));
            Assert.IsTrue(shift < 4.0, $"{sky}: {shift:0.00}° from the base");
        }
        var unclipped = new Vector3(0.1f, 0.2f, 0.5f);
        DisplayTransform.AgxLinear(unclipped, DisplayLook.Punchy, out float kept);
        Assert.AreEqual(DisplayTransform.PunchySaturation, kept);
        double own = HueDistance(DisplayTransform.Agx(unclipped, DisplayLook.Punchy), DisplayTransform.Agx(unclipped));
        Assert.IsTrue(own > 1.0 && own < 4.0, $"the look's own shift on an unclipped blue: {own:0.00}°");
    }

    /// <summary>The distance in degrees between two linear Rec.709 colours' Oklab hues.</summary>
    private static double HueDistance(Vector3 a, Vector3 b)
    {
        double d = Math.Abs(OklabHue(a) - OklabHue(b));
        return d > 180.0 ? 360.0 - d : d;
    }

    /// <summary>The Oklab hue angle in degrees of a linear Rec.709 colour (Björn Ottosson's matrices, 2020).</summary>
    private static double OklabHue(Vector3 rgb)
    {
        double l = Math.Cbrt(0.4122214708 * rgb.X + 0.5363325363 * rgb.Y + 0.0514459929 * rgb.Z);
        double m = Math.Cbrt(0.2119034982 * rgb.X + 0.6806995451 * rgb.Y + 0.1073969566 * rgb.Z);
        double s = Math.Cbrt(0.0883024619 * rgb.X + 0.2817188376 * rgb.Y + 0.6299787005 * rgb.Z);
        double a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double b = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        return Math.Atan2(b, a) * 180.0 / Math.PI;
    }

    [TestMethod]
    public void AgxPunchy_DeepensTheMidTones_AndAddsColour()
    {
        // Middle grey falls from sRGB 128 to 88 (why the look comes with a compensation), the greys stay grey and in order, and a
        // warm golden-hour colour two stops over grey comes out more saturated than the base gives it.
        CollectionAssert.AreEqual(new byte[] { 88, 88, 88 }, DisplayTransform.ToSrgb8([0.18f, 0.18f, 0.18f], 0.0, DisplayCurve.Agx, DisplayLook.Punchy));
        float previous = -1f;
        for (float x = 1e-3f; x < 1e4f; x *= 1.2f)
        {
            Vector3 c = DisplayTransform.Agx(new Vector3(x), DisplayLook.Punchy);
            Assert.IsTrue(c.Y >= previous && c.Y <= 1f && c.Y >= 0f, $"grey {x}");
            Assert.AreEqual(c.X, c.Y, 1e-3f, $"grey {x} stays grey");
            previous = c.Y;
        }
        static float Saturation(Vector3 c) => (Math.Max(c.X, Math.Max(c.Y, c.Z)) - Math.Min(c.X, Math.Min(c.Y, c.Z))) / Math.Max(c.X, Math.Max(c.Y, c.Z));
        var warm = new Vector3(0.72f, 0.45f, 0.2f);
        Assert.IsTrue(Saturation(DisplayTransform.Agx(warm, DisplayLook.Punchy)) > 1.3f * Saturation(DisplayTransform.Agx(warm)));
        Assert.AreEqual(Vector3.Zero, DisplayTransform.Agx(new Vector3(float.NaN), DisplayLook.Punchy) - DisplayTransform.Agx(Vector3.Zero, DisplayLook.Punchy));
    }

    [TestMethod]
    public void Looks_AreAgxs_TheBaseIsM1_10asCurve()
    {
        float[] colours = [0.18f, 0.18f, 0.18f, 4f, 2.5f, 1.2f, 0.02f, 0.05f, 0.12f, 0.9f, 0.05f, 0.4f];
        CollectionAssert.AreEqual(DisplayTransform.ToSrgb8(colours, 0.5, DisplayCurve.Agx), DisplayTransform.ToSrgb8(colours, 0.5, DisplayCurve.Agx, DisplayLook.Base));
        CollectionAssert.AreEqual(DisplayTransform.ToSrgb8(colours, 0.5, DisplayCurve.Agx), DisplayTransform.ToSrgb8(colours, 0.5, DisplayCurve.Agx, DisplayLook.None));
        CollectionAssert.AreEqual(DisplayTransform.ToSrgb8(colours, 0.5, DisplayCurve.Aces), DisplayTransform.ToSrgb8(colours, 0.5, DisplayCurve.Aces, DisplayLook.Punchy),
            "the ACES fit has no looks");
        Assert.AreEqual(DisplayLook.Punchy, DisplayTransform.LookOf(DisplayCurve.Agx, DisplayLook.Punchy));
        Assert.AreEqual(DisplayLook.Base, DisplayTransform.LookOf(DisplayCurve.Agx, DisplayLook.None));
        Assert.AreEqual(DisplayLook.None, DisplayTransform.LookOf(DisplayCurve.Aces, DisplayLook.Punchy));
        Assert.AreEqual(DisplayTransform.AgxPunchyName, DisplayTransform.NameOf(DisplayCurve.Agx, DisplayLook.Punchy));
        Assert.AreEqual(DisplayTransform.AgxName, DisplayTransform.NameOf(DisplayCurve.Agx, DisplayLook.Base));
        Assert.AreEqual(DisplayTransform.AcesName, DisplayTransform.NameOf(DisplayCurve.Aces, DisplayLook.Punchy));
    }

    [TestMethod]
    public void Agx_MatchesThreeJs()
    {
        // The browser's GPU and .NET round differently in the last bits, most where the sigmoid's terms (up to 40) cancel to about 1
        // in float: 1.4·10⁻⁵ at grey 8 when this was written, a three-hundredth of an 8-bit step.
        float worst = 0f;
        string where = string.Empty;
        foreach ((Vector3 input, Vector3 expected) in ThreeJs)
        {
            Vector3 difference = Vector3.Abs(DisplayTransform.Agx(input) - expected);
            float largest = Math.Max(difference.X, Math.Max(difference.Y, difference.Z));
            if (largest > worst)
                (worst, where) = (largest, input.ToString());
        }
        Assert.IsTrue(worst <= 2e-5f, $"worst {worst:E2} at {where}");
    }

    [TestMethod]
    public void Agx_PutsMiddleGreyMidScreen_AndStaysInRange()
    {
        byte[] grey = DisplayTransform.ToSrgb8([0.18f, 0.18f, 0.18f], 0.0, DisplayCurve.Agx);
        CollectionAssert.AreEqual(new byte[] { 128, 128, 128 }, grey);
        float previous = -1f;
        for (float x = 1e-4f; x < 1e4f; x *= 1.2f)
        {
            Vector3 c = DisplayTransform.Agx(new Vector3(x));
            Assert.IsTrue(c.Y >= previous && c.Y <= 1f && c.Y >= 0f, $"grey {x}");
            previous = c.Y;
        }
        Assert.AreEqual(Vector3.Zero, DisplayTransform.Agx(new Vector3(float.NaN)));
        Vector3 huge = DisplayTransform.Agx(new Vector3(float.PositiveInfinity, 1f, float.NegativeInfinity));
        Assert.IsTrue(float.IsFinite(huge.X) && float.IsFinite(huge.Y) && float.IsFinite(huge.Z));
    }

    [TestMethod]
    public void Agx_KeepsABrightWarmColourWarm_WhereAcesBleachesIt()
    {
        // The reason for AgX (vista-plan §10 M1.10a): a sunlit face at golden hour, two stops over middle grey and warm.
        var warm = new Vector3(8f, 4f, 1f);
        Vector3 agx = DisplayTransform.Agx(warm);
        var aces = new Vector3(DisplayTransform.Aces(warm.X), DisplayTransform.Aces(warm.Y), DisplayTransform.Aces(warm.Z));
        static float Saturation(Vector3 c) => (Math.Max(c.X, Math.Max(c.Y, c.Z)) - Math.Min(c.X, Math.Min(c.Y, c.Z))) / Math.Max(c.X, Math.Max(c.Y, c.Z));
        Assert.AreEqual(1f, aces.X, "ACES clips the red channel");
        Assert.IsTrue(Saturation(agx) > 1.3f * Saturation(aces), $"AgX {Saturation(agx)} against ACES {Saturation(aces)}");
        Assert.IsTrue(agx.X > agx.Y && agx.Y > agx.Z, "the hue's order is kept");
    }

    [TestMethod]
    public void Aces_MatchesNarkowiczsFormula()
    {
        // x(2.51x + 0.03) / (x(2.43x + 0.59) + 0.14), Narkowicz 2016, "ACES Filmic Tone Mapping Curve", evaluated in double.
        foreach ((float x, double expected) in new[]
        {
            (0.01f, 0.0037702797944479029), (0.18f, 0.26689892038949681), (0.5f, 0.61630695443645078),
            (1f, 0.80379746835443022), (2f, 0.914855072463768), (8f, 1.0),
        })
        {
            Assert.AreEqual(expected, DisplayTransform.Aces(x), 1e-6, $"x = {x}");
        }
    }

    [TestMethod]
    public void Curves_AreNamed_AndAgxIsTheDefault()
    {
        Assert.AreEqual(DisplayCurve.Agx, DisplayTransform.DefaultCurve);
        Assert.AreEqual(DisplayTransform.AgxName, DisplayTransform.NameOf(DisplayCurve.Agx));
        Assert.AreEqual(DisplayTransform.AcesName, DisplayTransform.NameOf(DisplayCurve.Aces));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DisplayTransform.NameOf(DisplayCurve.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DisplayTransform.ToSrgb8([0f, 0f, 0f], 0.0, DisplayCurve.None));
        Assert.ThrowsExactly<ArgumentException>(() => DisplayTransform.ToSrgb8([0f, 0f], 0.0, DisplayCurve.Agx));
    }
}
