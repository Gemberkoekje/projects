using System.Numerics;
using RayTracer;

namespace Vista.Output;

/// <summary>
/// From the integrator's CIE XYZ to files (vista-plan §5.7). XYZ → linear Rec.709 uses the engine's own
/// matrix (<see cref="JobSystem.ToSRGB"/>), so Vista and the maze agree on colour. The PNG display transform
/// is an <b>M0 placeholder</b> — exposure, then the Narkowicz ACES curve the engine's <c>Filmic</c> tone map
/// uses, then the sRGB transfer — until the AgX/ACES-style filmic transform of M1. The EXR stays
/// scene-referred (no exposure, no curve).
/// </summary>
public static class DisplayTransform
{
    /// <summary>Recorded in the sidecar so a PNG says how it was made.</summary>
    public const string Name = "exposure + ACES (Narkowicz) + sRGB, M0 placeholder";

    /// <summary>Converts interleaved XYZ to interleaved linear Rec.709 RGB.</summary>
    public static float[] XyzToLinearRec709(ReadOnlySpan<float> xyz)
    {
        if (xyz.Length % 3 != 0)
            throw new ArgumentException("XYZ data must hold three floats per pixel.", nameof(xyz));
        var rgb = new float[xyz.Length];
        for (int p = 0; p < xyz.Length; p += 3)
        {
            Vector3 c = JobSystem.ToSRGB(new Vector3(xyz[p], xyz[p + 1], xyz[p + 2]));
            rgb[p] = c.X;
            rgb[p + 1] = c.Y;
            rgb[p + 2] = c.Z;
        }
        return rgb;
    }

    /// <summary>Display-encodes interleaved linear RGB to 8-bit sRGB after <paramref name="exposureEv"/> stops of exposure.</summary>
    public static byte[] ToSrgb8(ReadOnlySpan<float> linearRgb, double exposureEv)
    {
        float scale = (float)Math.Pow(2.0, exposureEv);
        var bytes = new byte[linearRgb.Length];
        for (int i = 0; i < linearRgb.Length; i++)
            bytes[i] = EncodeSrgb(Aces(linearRgb[i] * scale));
        return bytes;
    }

    /// <summary>The Narkowicz ACES fit, clamped to [0, 1]; NaN and negatives map to 0.</summary>
    public static float Aces(float x)
    {
        if (float.IsNaN(x) || x <= 0f)
            return 0f;
        const float a = 2.51f, b = 0.03f, c = 2.43f, d = 0.59f, e = 0.14f;
        return Math.Clamp(x * (a * x + b) / (x * (c * x + d) + e), 0f, 1f);
    }

    /// <summary>The sRGB transfer of a linear value, clamped and rounded to 8 bits; NaN maps to 0.</summary>
    public static byte EncodeSrgb(float linear)
    {
        if (float.IsNaN(linear) || linear <= 0f)
            return 0;
        if (linear >= 1f)
            return 255;
        float v = linear <= 0.0031308f ? 12.92f * linear : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
        return (byte)(v * 255f + 0.5f);
    }
}
