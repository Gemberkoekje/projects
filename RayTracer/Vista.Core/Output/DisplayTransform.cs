using System.Numerics;
using RayTracer;

namespace Vista.Output;

/// <summary>The curve a PNG's exposed linear colour goes through before the sRGB transfer (vista-plan §5.7, §10 M1.10a).</summary>
public enum DisplayCurve
{
    /// <summary>Not chosen.</summary>
    None,

    /// <summary>
    /// AgX (<see cref="DisplayTransform.Agx"/>), the default since M1.10a: a log encoding and one sigmoid in a space whose primaries
    /// are pulled in, so a bright colour runs toward white keeping its hue instead of each channel clipping on its own.
    /// </summary>
    Agx,

    /// <summary>
    /// Narkowicz's fit of the ACES curve per channel (<see cref="DisplayTransform.Aces"/>), the default until M1.10a: on its
    /// shoulder a bright warm colour turns yellow, then white, as its channels saturate one by one.
    /// </summary>
    Aces,
}

/// <summary>
/// From the integrator's CIE XYZ to files (vista-plan §5.7). XYZ → linear Rec.709 uses the engine's own
/// matrix (<see cref="JobSystem.ToSRGB"/>), so Vista and the maze agree on colour. The PNG display transform is
/// exposure — metered since M1.3 (<see cref="AutoExposure"/>, and since M1.10a by default <see cref="IncidentMeter"/>) — then a
/// display curve (<see cref="DisplayCurve"/>: AgX by default since M1.10a, the ACES fit an option), then the sRGB transfer. The EXR
/// stays scene-referred (no exposure, no curve), in W·m⁻²·sr⁻¹·nm⁻¹.
/// </summary>
public static class DisplayTransform
{
    /// <summary>The curve a PNG gets unless another is asked for.</summary>
    public const DisplayCurve DefaultCurve = DisplayCurve.Agx;

    /// <summary>Recorded in the sidecar for <see cref="DisplayCurve.Agx"/>.</summary>
    public const string AgxName = "exposure + AgX (three.js r169: Blender's AgX base after Filament, Rec.2020 primaries, Wrensch's sigmoid fit) + sRGB";

    /// <summary>Recorded in the sidecar for <see cref="DisplayCurve.Aces"/>.</summary>
    public const string AcesName = "exposure + ACES fit (Narkowicz 2016, per channel) + sRGB";

    /// <summary>How a PNG made with <paramref name="curve"/> was display-encoded, for the sidecar.</summary>
    public static string NameOf(DisplayCurve curve) => curve switch
    {
        DisplayCurve.Agx => AgxName,
        DisplayCurve.Aces => AcesName,
        _ => throw new ArgumentOutOfRangeException(nameof(curve), curve, "Unknown display curve."),
    };

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

    /// <summary>
    /// Display-encodes interleaved linear RGB to 8-bit sRGB after <paramref name="exposureEv"/> stops of exposure, through
    /// <paramref name="curve"/>.
    /// </summary>
    public static byte[] ToSrgb8(ReadOnlySpan<float> linearRgb, double exposureEv, DisplayCurve curve)
    {
        if (linearRgb.Length % 3 != 0)
            throw new ArgumentException("RGB data must hold three floats per pixel.", nameof(linearRgb));
        float scale = (float)Math.Pow(2.0, exposureEv);
        var bytes = new byte[linearRgb.Length];
        switch (curve)
        {
            case DisplayCurve.Agx:
                for (int p = 0; p < linearRgb.Length; p += 3)
                {
                    Vector3 c = Agx(new Vector3(linearRgb[p], linearRgb[p + 1], linearRgb[p + 2]) * scale);
                    bytes[p] = EncodeSrgb(c.X);
                    bytes[p + 1] = EncodeSrgb(c.Y);
                    bytes[p + 2] = EncodeSrgb(c.Z);
                }
                break;
            case DisplayCurve.Aces:
                for (int i = 0; i < linearRgb.Length; i++)
                    bytes[i] = EncodeSrgb(Aces(linearRgb[i] * scale));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(curve), curve, "Unknown display curve.");
        }
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

    // three.js r169's constants. GLSL's mat3(c0, c1, c2) takes columns, so each matrix here is its listing transposed to rows.
    private static readonly Matrix3 Rec709ToRec2020 = new(
        0.6274f, 0.3293f, 0.0433f,
        0.0691f, 0.9195f, 0.0113f,
        0.0164f, 0.0880f, 0.8956f);

    private static readonly Matrix3 Rec2020ToRec709 = new(
        1.6605f, -0.5876f, -0.0728f,
        -0.1246f, 1.1329f, -0.0083f,
        -0.0182f, -0.1006f, 1.1187f);

    private static readonly Matrix3 AgxInset = new(
        0.856627153315983f, 0.0951212405381588f, 0.0482516061458583f,
        0.137318972929847f, 0.761241990602591f, 0.101439036467562f,
        0.11189821299995f, 0.0767994186031903f, 0.811302368396859f);

    private static readonly Matrix3 AgxOutset = new(
        1.1271005818144368f, -0.11060664309660323f, -0.016493938717834573f,
        -0.1413297634984383f, 1.157823702216272f, -0.016493938717834257f,
        -0.14132976349843826f, -0.11060664309660294f, 1.2519364065950405f);

    /// <summary>log₂ of 2⁻¹⁰ · 0.18: the darkest value AgX encodes, 10 stops under middle grey.</summary>
    private const float AgxMinEv = -12.47393f;

    /// <summary>log₂ of 2⁶·⁵ · 0.18: the brightest, 6.5 stops over middle grey.</summary>
    private const float AgxMaxEv = 4.026069f;

    /// <summary>
    /// AgX (Troy Sobotka's base transform, as Blender 4 ships it), ported line for line from three.js r169's
    /// <c>AgXToneMapping</c> (<c>src/renderers/shaders/ShaderChunk/tonemapping_pars_fragment.glsl.js</c>, MIT licence), which follows
    /// Filament's (google/filament#7236): linear Rec.709 to Rec.2020, AgX's inset (the primaries pulled in), log₂ encoded from
    /// 10 stops under middle grey to 6.5 over it, Benjamin Wrensch's sixth-order fit of AgX's default sigmoid, the outset, a 2.2
    /// power back to linear, back to Rec.709, clamped to [0, 1]. Linear Rec.709 in and out, as three.js's; the sRGB transfer
    /// follows (<see cref="EncodeSrgb"/>). NaN maps to 0. Middle grey (0.18) comes out at 0.2145, sRGB 128.
    /// </summary>
    public static Vector3 Agx(Vector3 linear)
    {
        Vector3 c = AgxInset.Times(Rec709ToRec2020.Times(Finite(linear)));
        c = new Vector3(AgxEncode(c.X), AgxEncode(c.Y), AgxEncode(c.Z));
        c = AgxOutset.Times(new Vector3(AgxSigmoid(c.X), AgxSigmoid(c.Y), AgxSigmoid(c.Z)));
        c = new Vector3(MathF.Pow(Math.Max(c.X, 0f), 2.2f), MathF.Pow(Math.Max(c.Y, 0f), 2.2f), MathF.Pow(Math.Max(c.Z, 0f), 2.2f));
        c = Rec2020ToRec709.Times(c);
        return Vector3.Clamp(c, Vector3.Zero, Vector3.One);
    }

    /// <summary>One channel log₂ encoded onto [0, 1] (zero and negatives at the bottom).</summary>
    private static float AgxEncode(float x) => Math.Clamp((MathF.Log2(Math.Max(x, 1e-10f)) - AgxMinEv) / (AgxMaxEv - AgxMinEv), 0f, 1f);

    /// <summary>Wrensch's polynomial fit of AgX's default contrast sigmoid ("mean error² 3.67·10⁻⁶"), on the encoded value.</summary>
    private static float AgxSigmoid(float x)
    {
        float x2 = x * x;
        float x4 = x2 * x2;
        return 15.5f * x4 * x2 - 40.14f * x4 * x + 31.96f * x4 - 6.868f * x2 * x + 0.4298f * x2 + 0.1191f * x - 0.00232f;
    }

    /// <summary><paramref name="v"/> with NaN as 0 and infinities as the largest floats, so the matrices never make a NaN.</summary>
    private static Vector3 Finite(Vector3 v)
    {
        static float F(float x) => float.IsNaN(x) ? 0f : Math.Clamp(x, -float.MaxValue, float.MaxValue);
        return new Vector3(F(v.X), F(v.Y), F(v.Z));
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

    /// <summary>A row-major 3×3 matrix of floats.</summary>
    private readonly record struct Matrix3(float M11, float M12, float M13, float M21, float M22, float M23, float M31, float M32, float M33)
    {
        public Vector3 Times(Vector3 v) => new(
            M11 * v.X + M12 * v.Y + M13 * v.Z,
            M21 * v.X + M22 * v.Y + M23 * v.Z,
            M31 * v.X + M32 * v.Y + M33 * v.Z);
    }
}
