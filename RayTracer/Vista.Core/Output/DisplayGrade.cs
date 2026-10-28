using System.Numerics;
using RayTracer;

namespace Vista.Output;

/// <summary>
/// The camera's choices between the image's light and the display curve (vista-plan §10 M1.17): a white balance and the punchy
/// look's margin short of the gamut's edge. A render takes Vista's camera's (<see cref="Default"/>: 5500 K and 0.03, the user's picks);
/// <see cref="None"/> is M1.11c's bytes: the display's own white (D65) and the look's saturation held back only where a channel would go
/// below 0. The EXR never changes.
/// </summary>
/// <param name="WhiteBalanceKelvin">
/// The colour temperature that renders neutral, as a camera's Kelvin setting: a Planckian light of this temperature comes out as
/// the display's white (<see cref="WhiteBalance"/>); 0 for none, the display's own D65.
/// </param>
/// <param name="SkyMargin">
/// How far short of the gamut's edge the punchy look's saturation stops, as a share of the colour's luminance: no Rec.709 channel
/// below this times the colour's Y (<see cref="DisplayTransform"/>); 0 for M1.11c's "just enough". Only the punchy look has it.
/// </param>
public readonly record struct DisplayGrade(double WhiteBalanceKelvin, double SkyMargin)
{
    /// <summary>The lowest Kelvin a white balance takes (the end of the Planckian fit).</summary>
    public const double MinKelvin = 1667.0;

    /// <summary>The highest Kelvin a white balance takes.</summary>
    public const double MaxKelvin = 25000.0;

    /// <summary>The widest margin: half the colour's luminance would grey a deep sky out entirely.</summary>
    public const double MaxSkyMargin = 0.5;

    /// <summary>No white balance and no margin: M1.11c's bytes.</summary>
    public static DisplayGrade None => default;

    /// <summary>Vista's camera's grade (<see cref="CameraSettings"/>): a daylight white balance of 5500 K and a 0.03 margin.</summary>
    public static DisplayGrade Default => new(CameraSettings.WhiteBalanceKelvin, CameraSettings.SkyMargin);

    /// <summary>True when this grade changes nothing.</summary>
    public bool IsNone => !HasWhiteBalance && !(SkyMargin > 0.0);

    /// <summary>True when the grade balances the white (<see cref="WhiteBalanceKelvin"/> is not 0).</summary>
    public bool HasWhiteBalance => WhiteBalanceKelvin > 0.0 || WhiteBalanceKelvin < 0.0;

    /// <summary>The problems with this grade's values, empty when it is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (HasWhiteBalance && !(WhiteBalanceKelvin >= MinKelvin && WhiteBalanceKelvin <= MaxKelvin))
            problems.Add($"The white balance must be 0 (none) or {MinKelvin:0}–{MaxKelvin:0} K, got {WhiteBalanceKelvin}.");
        if (!(SkyMargin >= 0.0 && SkyMargin <= MaxSkyMargin))
            problems.Add($"The sky margin must be 0–{MaxSkyMargin}, got {SkyMargin}.");
        return problems;
    }

    /// <summary>
    /// The linear Rec.709 → linear Rec.709 matrix of the white balance, row-major: identity for none. A Planckian light of
    /// <see cref="WhiteBalanceKelvin"/> (its chromaticity from Kim et al.'s fit, <see cref="PlanckianChromaticity"/>) is adapted to
    /// the display's white by Bradford's transform, in XYZ, between the engine's own Rec.709 matrices (so the display's white is
    /// exactly the white the engine's XYZ → Rec.709 maps to (1, 1, 1)). A white of luminance 1 keeps luminance 1.
    /// </summary>
    public double[] WhiteBalance()
    {
        if (!HasWhiteBalance)
            return [1, 0, 0, 0, 1, 0, 0, 0, 1];
        double[] toRec709 = EngineToRec709();
        double[] toXyz = Invert(toRec709);
        double[] displayWhite = Times(toXyz, [1.0, 1.0, 1.0]);
        displayWhite = [displayWhite[0] / displayWhite[1], 1.0, displayWhite[2] / displayWhite[1]];
        (double x, double y) = PlanckianChromaticity(WhiteBalanceKelvin);
        double[] source = [x / y, 1.0, (1.0 - x - y) / y];
        double[] coneSource = Times(Bradford, source);
        double[] coneDisplay = Times(Bradford, displayWhite);
        double[] scale = [coneDisplay[0] / coneSource[0], 0, 0, 0, coneDisplay[1] / coneSource[1], 0, 0, 0, coneDisplay[2] / coneSource[2]];
        double[] adapt = Multiply(Invert(Bradford), Multiply(scale, Bradford));
        return Multiply(toRec709, Multiply(adapt, toXyz));
    }

    /// <summary>
    /// The CIE 1931 chromaticity of a Planckian (black-body) light of <paramref name="kelvin"/>, from Kim et al.'s cubic fit
    /// (US patent 7,024,034; within about 0.001 of the locus from 1667 to 25 000 K).
    /// </summary>
    public static (double X, double Y) PlanckianChromaticity(double kelvin)
    {
        if (!(kelvin >= MinKelvin && kelvin <= MaxKelvin))
            throw new ArgumentOutOfRangeException(nameof(kelvin), kelvin, $"The fit holds from {MinKelvin:0} to {MaxKelvin:0} K.");
        double t = kelvin, t2 = t * t, t3 = t2 * t;
        double x = t <= 4000.0
            ? -0.2661239e9 / t3 - 0.2343589e6 / t2 + 0.8776956e3 / t + 0.179910
            : -3.0258469e9 / t3 + 2.1070379e6 / t2 + 0.2226347e3 / t + 0.240390;
        double x2 = x * x, x3 = x2 * x;
        if (t <= 2222.0)
            return (x, -1.1063814 * x3 - 1.34811020 * x2 + 2.18555832 * x - 0.20219683);
        if (t <= 4000.0)
            return (x, -0.9549476 * x3 - 1.37418593 * x2 + 2.09137015 * x - 0.16748867);
        return (x, 3.0817580 * x3 - 5.87338670 * x2 + 3.75112997 * x - 0.37001483);
    }

    /// <summary>Bradford's cone response matrix, row-major.</summary>
    private static readonly double[] Bradford =
    [
        0.8951, 0.2664, -0.1614,
        -0.7502, 1.7135, 0.0367,
        0.0389, -0.0685, 1.0296,
    ];

    /// <summary>The engine's XYZ → linear Rec.709 (<see cref="JobSystem.ToSRGB"/>) as a row-major matrix, read off its columns.</summary>
    private static double[] EngineToRec709()
    {
        Vector3 cx = JobSystem.ToSRGB(Vector3.UnitX), cy = JobSystem.ToSRGB(Vector3.UnitY), cz = JobSystem.ToSRGB(Vector3.UnitZ);
        return [cx.X, cy.X, cz.X, cx.Y, cy.Y, cz.Y, cx.Z, cy.Z, cz.Z];
    }

    private static double[] Times(double[] m, double[] v) =>
    [
        m[0] * v[0] + m[1] * v[1] + m[2] * v[2],
        m[3] * v[0] + m[4] * v[1] + m[5] * v[2],
        m[6] * v[0] + m[7] * v[1] + m[8] * v[2],
    ];

    private static double[] Multiply(double[] a, double[] b)
    {
        var c = new double[9];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
                c[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        }
        return c;
    }

    private static double[] Invert(double[] m)
    {
        double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
        double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        return
        [
            (e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det,
            (f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det,
            (d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det,
        ];
    }
}
