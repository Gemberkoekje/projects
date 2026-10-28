using System.Numerics;
using RayTracer;
using Vista.Cli;
using Vista.Output;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// The camera's choices before the display curve (vista-plan §10 M1.17): a white balance in Kelvin, the punchy look's margin short of
/// the gamut's edge, and how far the incident meter follows the light at dusk; none of them by default, which writes M1.11c's bytes.
/// </summary>
[TestClass]
public sealed class DisplayGradeTests
{
    /// <summary>A frame of linear Rec.709 with greys, saturated colours, a deep blue and the extremes.</summary>
    private static float[] Frame(int width, int height)
    {
        var rgb = new float[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 3;
                float t = (float)x / width;
                rgb[p] = (x + y) % 7 == 0 ? 0.002f : 0.03f + 0.4f * t * t;
                rgb[p + 1] = 0.04f + 0.25f * t + 0.01f * y;
                float blue = (x + y) % 13 == 0 ? 300f : 0.2f + 0.05f * y;
                rgb[p + 2] = (x + y) % 11 == 0 ? 0f : blue;
            }
        }
        return rgb;
    }

    private static PreviewRequest Parse(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
        return request!;
    }

    private static string Error(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsFalse(PreviewCommandLine.TryParse(args, out _, out IReadOnlyList<string> errors));
        return string.Join(" | ", errors);
    }

    [TestMethod]
    public void NoGrade_WritesM1_11csBytes()
    {
        float[] frame = Frame(40, 24);
        PixelWindow window = PixelWindow.Full(40, 24);
        foreach (DisplayLook look in new[] { DisplayLook.Base, DisplayLook.Punchy })
        {
            CollectionAssert.AreEqual(DisplayTransform.ToSrgb8(frame, 0.4, DisplayCurve.Agx, look),
                DisplayTransform.ToSrgb8(frame, 0.4, DisplayCurve.Agx, look, DisplayGrade.None), look.ToString());
            CollectionAssert.AreEqual(DisplayTransform.ToSrgb8(frame, 0.4, DisplayCurve.Agx, look, PngDither.Triangular, window),
                DisplayTransform.ToSrgb8(frame, 0.4, DisplayCurve.Agx, look, DisplayGrade.None, PngDither.Triangular, window), look.ToString());
            CollectionAssert.AreEqual(DisplayTransform.ToSrgb16(frame, 0.4, DisplayCurve.Agx, look),
                DisplayTransform.ToSrgb16(frame, 0.4, DisplayCurve.Agx, look, DisplayGrade.None), look.ToString());
        }
        Assert.IsTrue(DisplayGrade.None.IsNone);
        CollectionAssert.AreEqual(new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, DisplayGrade.None.WhiteBalance());
    }

    [TestMethod]
    public void ThePlanckianFit_MatchesTheLocus()
    {
        // The locus from CIE 1931's functions (Wyszecki and Stiles): 2856 K is illuminant A; 5000 and 6500 K on the Planckian locus.
        (double x, double y) a = DisplayGrade.PlanckianChromaticity(2856.0);
        Assert.AreEqual(0.4476, a.x, 0.001);
        Assert.AreEqual(0.4074, a.y, 0.001);
        (double x, double y) k5000 = DisplayGrade.PlanckianChromaticity(5000.0);
        Assert.AreEqual(0.3451, k5000.x, 0.001);
        Assert.AreEqual(0.3516, k5000.y, 0.001);
        (double x, double y) k6500 = DisplayGrade.PlanckianChromaticity(6500.0);
        Assert.AreEqual(0.3135, k6500.x, 0.001);
        Assert.AreEqual(0.3236, k6500.y, 0.001);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DisplayGrade.PlanckianChromaticity(1000.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DisplayGrade.PlanckianChromaticity(30000.0));
    }

    [TestMethod]
    public void AWhiteBalance_RendersItsOwnLightNeutral_AndKeepsItsLuminance()
    {
        foreach (double kelvin in new[] { 2500.0, 4400.0, 5000.0, 5500.0, 9000.0 })
        {
            (double x, double y) = DisplayGrade.PlanckianChromaticity(kelvin);
            Vector3 light = JobSystem.ToSRGB(new Vector3((float)(x / y), 1f, (float)((1.0 - x - y) / y)));
            double[] m = new DisplayGrade(kelvin, 0.0).WhiteBalance();
            double r = m[0] * light.X + m[1] * light.Y + m[2] * light.Z;
            double g = m[3] * light.X + m[4] * light.Y + m[5] * light.Z;
            double b = m[6] * light.X + m[7] * light.Y + m[8] * light.Z;
            Assert.AreEqual(1.0, r, 2e-4, $"{kelvin} K red");
            Assert.AreEqual(1.0, g, 2e-4, $"{kelvin} K green");
            Assert.AreEqual(1.0, b, 2e-4, $"{kelvin} K blue");
        }
        // A light warmer than the balance stays warm, a bluer one cool: 3000 K under a 5500 K balance is orange, 9000 K blue.
        float[] warm = Light(3000.0), cool = Light(9000.0);
        byte[] warmed = DisplayTransform.ToSrgb8(warm, 0.0, DisplayCurve.Agx, DisplayLook.Base, new DisplayGrade(5500.0, 0.0));
        byte[] cooled = DisplayTransform.ToSrgb8(cool, 0.0, DisplayCurve.Agx, DisplayLook.Base, new DisplayGrade(5500.0, 0.0));
        Assert.IsTrue(warmed[0] > warmed[2] + 20, $"3000 K under 5500 K: {warmed[0]}, {warmed[1]}, {warmed[2]}");
        Assert.IsTrue(cooled[2] > cooled[0] + 10, $"9000 K under 5500 K: {cooled[0]}, {cooled[1]}, {cooled[2]}");
        byte[] neutral = DisplayTransform.ToSrgb8(Light(5500.0), 0.0, DisplayCurve.Agx, DisplayLook.Punchy, new DisplayGrade(5500.0, 0.0));
        Assert.IsTrue(Math.Abs(neutral[0] - neutral[2]) <= 1 && Math.Abs(neutral[0] - neutral[1]) <= 1, $"5500 K under its own balance: {neutral[0]}, {neutral[1]}, {neutral[2]}");
    }

    /// <summary>One pixel of a Planckian light of <paramref name="kelvin"/> at luminance 0.18, linear Rec.709.</summary>
    private static float[] Light(double kelvin)
    {
        (double x, double y) = DisplayGrade.PlanckianChromaticity(kelvin);
        Vector3 c = JobSystem.ToSRGB(new Vector3((float)(0.18 * x / y), 0.18f, (float)(0.18 * (1.0 - x - y) / y)));
        return [c.X, c.Y, c.Z];
    }

    [TestMethod]
    public void TheSkyMargin_KeepsEveryChannelItsShareOfTheLuminance()
    {
        // Colours over 12 stops and every hue, saturated to the edge of Rec.709 and beyond, as in M1.11c's check: with a margin, a colour
        // the look holds back keeps every channel at least the margin's share of its luminance, unless even the power alone (saturation 1)
        // does not; it never gets more saturation than without the margin, and some get less.
        int lessened = 0;
        foreach (float margin in new[] { 0.03f, 0.06f, 0.1f })
        {
            foreach (float level in new[] { 0.01f, 0.05f, 0.18f, 0.7f, 3f, 20f })
            {
                for (int h = 0; h < 24; h++)
                {
                    foreach (float chroma in new[] { 0.3f, 0.7f, 0.95f, 1.2f })
                    {
                        double angle = h * Math.PI / 12.0;
                        var direction = new Vector3((float)Math.Cos(angle), (float)Math.Cos(angle - 2.0 * Math.PI / 3.0), (float)Math.Cos(angle + 2.0 * Math.PI / 3.0));
                        Vector3 input = level * (Vector3.One + chroma * direction);
                        DisplayTransform.AgxLinear(input, DisplayLook.Punchy, 0f, out float edge);
                        Vector3 c = DisplayTransform.AgxLinear(input, DisplayLook.Punchy, margin, out float saturation);
                        float luminance = Math.Max(Vector3.Dot(c, new Vector3(0.2126f, 0.7152f, 0.0722f)), 0f);
                        float lowest = Math.Min(c.X, Math.Min(c.Y, c.Z));
                        Assert.IsTrue(saturation >= 1f && saturation <= edge, $"{input} margin {margin}: saturation {saturation}, {edge} without");
                        if (saturation > 1f)
                            Assert.IsTrue(lowest >= margin * luminance - 1e-5f, $"{input} margin {margin}: lowest {lowest}, luminance {luminance}");
                        if (saturation < edge)
                            lessened++;
                    }
                }
            }
        }
        Assert.IsTrue(lessened > 0, "the margin holds some colours back further");
        // A colour well inside the gamut keeps the full look.
        Vector3 grey = DisplayTransform.AgxLinear(new Vector3(0.2f, 0.18f, 0.16f), DisplayLook.Punchy, 0.1f, out float full);
        Assert.AreEqual(DisplayTransform.PunchySaturation, full);
        Assert.AreEqual(DisplayTransform.AgxLinear(new Vector3(0.2f, 0.18f, 0.16f), DisplayLook.Punchy, out _), grey);
    }

    [TestMethod]
    public void TheGrade_IsValidated()
    {
        Assert.AreEqual(0, new DisplayGrade(5500.0, 0.05).Validate().Count);
        Assert.AreEqual(1, new DisplayGrade(500.0, 0.0).Validate().Count);
        Assert.AreEqual(1, new DisplayGrade(0.0, 0.8).Validate().Count);
    }

    [TestMethod]
    public void TheCommandLine_SetsTheGradeAndTheAdaptation()
    {
        PreviewRequest plain = Parse();
        Assert.AreEqual(DisplayGrade.None, plain.Grade, "none by default");
        Assert.AreEqual(IncidentMeter.DefaultAdaptation, plain.Adaptation);
        Assert.AreEqual(5500.0, Parse("--white-balance", "5500K").Grade.WhiteBalanceKelvin);
        Assert.AreEqual(4400.0, Parse("--white-balance", "4400").Grade.WhiteBalanceKelvin);
        Assert.AreEqual(0.0, Parse("--white-balance", "none").Grade.WhiteBalanceKelvin);
        Assert.AreEqual(0.06, Parse("--sky-margin", "0.06").Grade.SkyMargin);
        Assert.AreEqual(0.5, Parse("--adaptation", "0.5").Adaptation);
        StringAssert.Contains(Error("--white-balance", "warm"), "--white-balance must be none or");
        StringAssert.Contains(Error("--white-balance", "900K"), "--white-balance must be none or");
        StringAssert.Contains(Error("--sky-margin", "0.9"), "--sky-margin must be a share");
        StringAssert.Contains(Error("--adaptation", "1.5"), "--adaptation must be from 0 to 1");
    }

    [TestMethod]
    public void ASheet_VariesTheGradeAndTheAdaptation()
    {
        var request = new PreviewRequest { RecipePath = "r.json", Seed = 1, OutPath = "p.png" };
        Assert.IsTrue(SheetAxis.TryParse("whiteBalance=none,5500K", out SheetAxis? balance, out string? problem), problem);
        Assert.AreEqual(5500.0, balance!.Apply(request, 1).Grade.WhiteBalanceKelvin);
        Assert.AreEqual(0.0, balance.Apply(request, 0).Grade.WhiteBalanceKelvin);
        Assert.IsTrue(SheetAxis.TryParse("skyMargin=0,0.1", out SheetAxis? margin, out problem), problem);
        Assert.AreEqual(0.1, margin!.Apply(request with { Grade = new DisplayGrade(5000.0, 0.0) }, 1).Grade.SkyMargin);
        Assert.AreEqual(5000.0, margin.Apply(request with { Grade = new DisplayGrade(5000.0, 0.0) }, 1).Grade.WhiteBalanceKelvin, "the other half of the grade stays");
        Assert.IsTrue(SheetAxis.TryParse("adaptation=1,0.5,0", out SheetAxis? adaptation, out problem), problem);
        Assert.AreEqual(0.0, adaptation!.Apply(request, 2).Adaptation);
        Assert.IsFalse(SheetAxis.TryParse("adaptation=1,2", out _, out problem));
        StringAssert.Contains(problem, "not an adaptation");
    }
}
