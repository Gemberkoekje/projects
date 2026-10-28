using System.Numerics;
using RayTracer;
using Vista.Rendering;
using Vista.World;

namespace Vista.Output;

/// <summary>The curve a PNG's exposed linear colour goes through before the sRGB transfer (vista-plan §5.7, §10 M1.10a).</summary>
public enum DisplayCurve
{
    /// <summary>Not chosen.</summary>
    None,

    /// <summary>
    /// AgX (<see cref="DisplayTransform.Agx(Vector3)"/>), the default since M1.10a: a log encoding and one sigmoid in a space whose primaries
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
/// A look on top of AgX's sigmoid (vista-plan §10 M1.11b): AgX's base is a neutral start meant for grading; a look grades it.
/// The ACES fit has none.
/// </summary>
public enum DisplayLook
{
    /// <summary>Not chosen, or a curve without looks (<see cref="DisplayCurve.Aces"/>).</summary>
    None,

    /// <summary>AgX's base, ungraded (M1.10a's).</summary>
    Base,

    /// <summary>
    /// AgX's "punchy" look (Blender's and Filament's): after the sigmoid, each channel to the power 1.35 and the saturation about
    /// the luma raised by 1.4 (<see cref="DisplayTransform.AgxPunchy"/>). Deeper mid-tones and more colour.
    /// </summary>
    Punchy,
}

/// <summary>How an 8-bit PNG's quantisation is dithered (vista-plan §10 M1.11c).</summary>
public enum PngDither
{
    /// <summary>Not chosen, or a PNG that is not quantised to 8 bits (a 16-bit PNG needs no dither).</summary>
    None,

    /// <summary>Each value rounded to the nearest step: M1.11b's bytes, banding and all.</summary>
    Off,

    /// <summary>
    /// A triangular (TPDF) dither of ±1 step in the sRGB-encoded value before the rounding, from a hash of the frame pixel and the
    /// channel (<see cref="DisplayTransform.DitherOffset"/>): zero-mean, the same bytes every run, and a loupe an exact crop of the frame.
    /// </summary>
    Triangular,
}

/// <summary>
/// From the integrator's CIE XYZ to files (vista-plan §5.7). XYZ → linear Rec.709 uses the engine's own
/// matrix (<see cref="JobSystem.ToSRGB"/>), so Vista and the maze agree on colour. The PNG display transform is
/// exposure — metered since M1.3 (<see cref="AutoExposure"/>, and since M1.10a by default <see cref="IncidentMeter"/>) — then a
/// display curve (<see cref="DisplayCurve"/>: AgX by default since M1.10a, the ACES fit an option), then the sRGB transfer, then
/// the quantisation: 8 bits with a dither by default since M1.11c (<see cref="PngDither"/>), or 16 bits. The EXR stays
/// scene-referred (no exposure, no curve), in W·m⁻²·sr⁻¹·nm⁻¹.
/// </summary>
public static class DisplayTransform
{
    /// <summary>The dither an 8-bit PNG gets unless another is asked for: the triangular dither since M1.11c.</summary>
    public const PngDither DefaultDither = PngDither.Triangular;

    /// <summary>The bit depth of a PNG unless another is asked for.</summary>
    public const int DefaultBitDepth = 8;

    /// <summary>
    /// The dither a PNG of <paramref name="bitDepth"/> bits actually gets when <paramref name="dither"/> is asked for: none at 16 bits,
    /// whose steps are 257 times finer than an 8-bit PNG's, and at 8 bits the one asked for (<see cref="PngDither.None"/> read as the
    /// default).
    /// </summary>
    public static PngDither DitherOf(int bitDepth, PngDither dither) => bitDepth switch
    {
        8 => dither == PngDither.None ? DefaultDither : dither,
        16 => PngDither.None,
        _ => throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "A PNG is 8 or 16 bits a channel."),
    };

    /// <summary>The curve a PNG gets unless another is asked for.</summary>
    public const DisplayCurve DefaultCurve = DisplayCurve.Agx;

    /// <summary>Recorded in the sidecar for <see cref="DisplayCurve.Agx"/>.</summary>
    public const string AgxName = "exposure + AgX (three.js r169: Blender's AgX base after Filament, Rec.2020 primaries, Wrensch's sigmoid fit) + sRGB";

    /// <summary>Recorded in the sidecar for <see cref="DisplayCurve.Agx"/> with <see cref="DisplayLook.Punchy"/>.</summary>
    public const string AgxPunchyName = "exposure + AgX punchy (three.js r169's AgX, Filament's punchy look after the sigmoid: power 1.35, saturation 1.4 held back where a channel would clip) + sRGB";

    /// <summary>Recorded in the sidecar for <see cref="DisplayCurve.Aces"/>.</summary>
    public const string AcesName = "exposure + ACES fit (Narkowicz 2016, per channel) + sRGB";

    /// <summary>
    /// The look AgX gets unless another is asked for: Vista's camera's (<see cref="CameraSettings.Look"/>), punchy since M1.11b, chosen by
    /// sheet with the incident meter's compensation and kept at M1.17 with a margin short of the gamut's edge (<see cref="DisplayGrade"/>).
    /// </summary>
    public const DisplayLook DefaultLook = CameraSettings.Look;

    /// <summary>How a PNG made with <paramref name="curve"/> was display-encoded, for the sidecar.</summary>
    public static string NameOf(DisplayCurve curve) => NameOf(curve, DisplayLook.Base);

    /// <summary>How a PNG made with <paramref name="curve"/> and <paramref name="look"/> was display-encoded, for the sidecar.</summary>
    public static string NameOf(DisplayCurve curve, DisplayLook look) => (curve, LookOf(curve, look)) switch
    {
        (DisplayCurve.Agx, DisplayLook.Punchy) => AgxPunchyName,
        (DisplayCurve.Agx, _) => AgxName,
        (DisplayCurve.Aces, _) => AcesName,
        _ => throw new ArgumentOutOfRangeException(nameof(curve), curve, "Unknown display curve."),
    };

    /// <summary>
    /// The look <paramref name="curve"/> actually applies when <paramref name="look"/> is asked for: AgX's own (<see cref="DisplayLook.None"/>
    /// read as its base), and none for the ACES fit, which has no looks.
    /// </summary>
    public static DisplayLook LookOf(DisplayCurve curve, DisplayLook look) => curve switch
    {
        DisplayCurve.Agx => look == DisplayLook.None ? DisplayLook.Base : look,
        _ => DisplayLook.None,
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
    public static byte[] ToSrgb8(ReadOnlySpan<float> linearRgb, double exposureEv, DisplayCurve curve) =>
        ToSrgb8(linearRgb, exposureEv, curve, DisplayLook.Base);

    /// <summary>
    /// Display-encodes interleaved linear RGB to 8-bit sRGB after <paramref name="exposureEv"/> stops of exposure, through
    /// <paramref name="curve"/> with <paramref name="look"/> (AgX's only; the ACES fit ignores it), each value rounded to the nearest
    /// step: no dither, M1.11b's bytes.
    /// </summary>
    public static byte[] ToSrgb8(ReadOnlySpan<float> linearRgb, double exposureEv, DisplayCurve curve, DisplayLook look) =>
        ToSrgb8(linearRgb, exposureEv, curve, look, DisplayGrade.None);

    /// <summary>
    /// <see cref="ToSrgb8(ReadOnlySpan{float}, double, DisplayCurve, DisplayLook)"/> with <paramref name="grade"/>'s white balance and
    /// margin (vista-plan §10 M1.17).
    /// </summary>
    public static byte[] ToSrgb8(ReadOnlySpan<float> linearRgb, double exposureEv, DisplayCurve curve, DisplayLook look, DisplayGrade grade)
    {
        float[] encoded = Encode(linearRgb, exposureEv, curve, look, grade);
        var bytes = new byte[encoded.Length];
        for (int i = 0; i < encoded.Length; i++)
            bytes[i] = Quantise8(encoded[i]);
        return bytes;
    }

    /// <summary>
    /// <see cref="ToSrgb8(ReadOnlySpan{float}, double, DisplayCurve, DisplayLook)"/> with <paramref name="dither"/> before the
    /// quantisation (vista-plan §10 M1.11c). The image is <paramref name="window"/> of its frame, so the dither is keyed by the frame
    /// pixel and a loupe stays an exact crop of the frame. <see cref="PngDither.Off"/> writes the undithered bytes.
    /// </summary>
    /// <exception cref="ArgumentException">The window does not hold as many pixels as <paramref name="linearRgb"/>.</exception>
    public static byte[] ToSrgb8(ReadOnlySpan<float> linearRgb, double exposureEv, DisplayCurve curve, DisplayLook look, PngDither dither, PixelWindow window) =>
        ToSrgb8(linearRgb, exposureEv, curve, look, DisplayGrade.None, dither, window);

    /// <summary>
    /// <see cref="ToSrgb8(ReadOnlySpan{float}, double, DisplayCurve, DisplayLook, PngDither, PixelWindow)"/> with <paramref name="grade"/>'s
    /// white balance and margin (vista-plan §10 M1.17).
    /// </summary>
    /// <exception cref="ArgumentException">The window does not hold as many pixels as <paramref name="linearRgb"/>.</exception>
    public static byte[] ToSrgb8(ReadOnlySpan<float> linearRgb, double exposureEv, DisplayCurve curve, DisplayLook look, DisplayGrade grade, PngDither dither,
        PixelWindow window)
    {
        if ((long)window.Pixels * 3 != linearRgb.Length)
            throw new ArgumentException($"The window {window} holds {window.Pixels} pixels, the image {linearRgb.Length / 3}.", nameof(window));
        if (DitherOf(8, dither) == PngDither.Off)
            return ToSrgb8(linearRgb, exposureEv, curve, look, grade);
        float[] encoded = Encode(linearRgb, exposureEv, curve, look, grade);
        var bytes = new byte[encoded.Length];
        for (int q = 0; q < window.Pixels; q++)
        {
            int x = window.X + q % window.Width;
            int y = window.Y + q / window.Width;
            for (int channel = 0; channel < 3; channel++)
                bytes[q * 3 + channel] = Dithered8(encoded[q * 3 + channel], x, y, channel);
        }
        return bytes;
    }

    /// <summary>
    /// Display-encodes interleaved linear RGB to 16-bit sRGB (vista-plan §10 M1.11c): the same exposure, curve and look as
    /// <see cref="ToSrgb8(ReadOnlySpan{float}, double, DisplayCurve, DisplayLook)"/>, each value rounded to the nearest of 65 535
    /// steps, which needs no dither.
    /// </summary>
    public static ushort[] ToSrgb16(ReadOnlySpan<float> linearRgb, double exposureEv, DisplayCurve curve, DisplayLook look) =>
        ToSrgb16(linearRgb, exposureEv, curve, look, DisplayGrade.None);

    /// <summary>
    /// <see cref="ToSrgb16(ReadOnlySpan{float}, double, DisplayCurve, DisplayLook)"/> with <paramref name="grade"/>'s white balance and
    /// margin (vista-plan §10 M1.17).
    /// </summary>
    public static ushort[] ToSrgb16(ReadOnlySpan<float> linearRgb, double exposureEv, DisplayCurve curve, DisplayLook look, DisplayGrade grade)
    {
        float[] encoded = Encode(linearRgb, exposureEv, curve, look, grade);
        var samples = new ushort[encoded.Length];
        for (int i = 0; i < encoded.Length; i++)
            samples[i] = (ushort)(encoded[i] * 65535f + 0.5f);
        return samples;
    }

    /// <summary>
    /// The sRGB-encoded values, on [0, 1], of interleaved linear RGB after <paramref name="exposureEv"/> stops of exposure through
    /// <paramref name="curve"/> with <paramref name="look"/>: everything before the quantisation. Pixels are independent, so they are
    /// split across threads in blocks, which changes no bit.
    /// </summary>
    internal static float[] Encode(ReadOnlySpan<float> linearRgb, double exposureEv, DisplayCurve curve, DisplayLook look) =>
        Encode(linearRgb, exposureEv, curve, look, DisplayGrade.None);

    /// <summary>
    /// <see cref="Encode(ReadOnlySpan{float}, double, DisplayCurve, DisplayLook)"/> with <paramref name="grade"/> (vista-plan §10 M1.17):
    /// the white balance before the exposure, the punchy look's margin in its saturation. <see cref="DisplayGrade.None"/> changes no bit.
    /// </summary>
    internal static float[] Encode(ReadOnlySpan<float> linearRgb, double exposureEv, DisplayCurve curve, DisplayLook look, DisplayGrade grade)
    {
        if (linearRgb.Length % 3 != 0)
            throw new ArgumentException("RGB data must hold three floats per pixel.", nameof(linearRgb));
        float scale = (float)Math.Pow(2.0, exposureEv);
        float[] input = linearRgb.ToArray();
        if (grade.HasWhiteBalance)
            Balance(input, grade.WhiteBalance());
        float margin = (float)grade.SkyMargin;
        var encoded = new float[input.Length];
        int pixels = input.Length / 3;
        switch (curve)
        {
            case DisplayCurve.Agx:
                DisplayLook agxLook = LookOf(curve, look);
                ForBlocks(pixels, (first, end) =>
                {
                    for (int p = first * 3; p < end * 3; p += 3)
                    {
                        Vector3 c = Vector3.Clamp(AgxLinear(new Vector3(input[p], input[p + 1], input[p + 2]) * scale, agxLook, margin, out _), Vector3.Zero, Vector3.One);
                        encoded[p] = Transfer(c.X);
                        encoded[p + 1] = Transfer(c.Y);
                        encoded[p + 2] = Transfer(c.Z);
                    }
                });
                break;
            case DisplayCurve.Aces:
                ForBlocks(pixels, (first, end) =>
                {
                    for (int i = first * 3; i < end * 3; i++)
                        encoded[i] = Transfer(Aces(input[i] * scale));
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(curve), curve, "Unknown display curve.");
        }
        return encoded;
    }

    /// <summary>Pixels per block of <see cref="ForBlocks"/>: small images stay on one thread.</summary>
    private const int BlockPixels = 16384;

    /// <summary>Runs <paramref name="body"/> over [first, end) blocks of <paramref name="pixels"/>, in parallel.</summary>
    private static void ForBlocks(int pixels, Action<int, int> body)
    {
        int blocks = (pixels + BlockPixels - 1) / BlockPixels;
        if (blocks <= 1)
        {
            body(0, pixels);
            return;
        }
        Parallel.For(0, blocks, b => body(b * BlockPixels, Math.Min(pixels, (b + 1) * BlockPixels)));
    }

    /// <summary>Applies the row-major white balance <paramref name="matrix"/> to interleaved linear Rec.709, in place.</summary>
    private static void Balance(float[] rgb, double[] matrix)
    {
        var m = new Matrix3((float)matrix[0], (float)matrix[1], (float)matrix[2], (float)matrix[3], (float)matrix[4], (float)matrix[5],
            (float)matrix[6], (float)matrix[7], (float)matrix[8]);
        ForBlocks(rgb.Length / 3, (first, end) =>
        {
            for (int p = first * 3; p < end * 3; p += 3)
            {
                Vector3 c = m.Times(new Vector3(rgb[p], rgb[p + 1], rgb[p + 2]));
                (rgb[p], rgb[p + 1], rgb[p + 2]) = (c.X, c.Y, c.Z);
            }
        });
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
    public static Vector3 Agx(Vector3 linear) => Agx(linear, DisplayLook.Base);

    /// <summary>
    /// <see cref="Agx(Vector3)"/> with <paramref name="look"/> graded on the sigmoid's output, before the outset, where Filament's
    /// <c>AgxToneMapper</c> applies its looks (<see cref="AgxPunchy"/>; its saturation gamut-safe since M1.11c,
    /// <see cref="AgxLinear(Vector3, DisplayLook, float, out float)"/>).
    /// </summary>
    public static Vector3 Agx(Vector3 linear, DisplayLook look) => Vector3.Clamp(AgxLinear(linear, look, out _), Vector3.Zero, Vector3.One);

    /// <summary>
    /// <see cref="Agx(Vector3, DisplayLook)"/> before its final clamp: the PNG's linear Rec.709, where a channel below 0 is one the
    /// clamp would cut off. Under the punchy look, <paramref name="saturation"/> is the look's saturation this colour got
    /// (<see cref="GamutSafePunchy"/>); otherwise 1.
    /// </summary>
    internal static Vector3 AgxLinear(Vector3 linear, DisplayLook look, out float saturation) => AgxLinear(linear, look, 0f, out saturation);

    /// <summary>
    /// <see cref="AgxLinear(Vector3, DisplayLook, out float)"/> with the punchy look's saturation held back <paramref name="margin"/>
    /// short of the gamut's edge (<see cref="DisplayGrade.SkyMargin"/>): no Rec.709 channel below the margin times the colour's luminance.
    /// </summary>
    internal static Vector3 AgxLinear(Vector3 linear, DisplayLook look, float margin, out float saturation)
    {
        Vector3 c = AgxInset.Times(Rec709ToRec2020.Times(Finite(linear)));
        c = new Vector3(AgxEncode(c.X), AgxEncode(c.Y), AgxEncode(c.Z));
        c = new Vector3(AgxSigmoid(c.X), AgxSigmoid(c.Y), AgxSigmoid(c.Z));
        saturation = 1f;
        return look switch
        {
            DisplayLook.Base or DisplayLook.None => AgxDisplay(c, 0f, out _),
            DisplayLook.Punchy => GamutSafePunchy(c, margin, out saturation),
            _ => throw new ArgumentOutOfRangeException(nameof(look), look, "Unknown look."),
        };
    }

    /// <summary>The punchy look's power (Filament's).</summary>
    internal const float PunchyPower = 1.35f;

    /// <summary>The punchy look's saturation (Filament's), the most <see cref="GamutSafePunchy"/> gives a colour.</summary>
    internal const float PunchySaturation = 1.4f;

    /// <summary>Halvings of the saturation's bracket in <see cref="GamutSafePunchy"/>: 0.4 / 2¹⁴ ≈ 2.4·10⁻⁵ at the most.</summary>
    private const int SaturationBisections = 14;

    /// <summary>
    /// The punchy look with its saturation held back where it would clip (vista-plan §10 M1.11c), then the rest of the curve: the
    /// PNG's linear Rec.709, before the clamp. A colour whose Rec.709 keeps every channel at or above 0 under Filament's
    /// <see cref="PunchySaturation"/> gets exactly Filament's look. Otherwise the saturation is lowered just enough that no channel
    /// goes below 0, but never below 1 (the power alone; a colour that clips even there is not the look's to fix), so the colour
    /// moves toward its luma, keeping its hue, instead of losing its lowest channel to the clamp.
    ///
    /// <para>The look is linear in the saturation and so is the outset after it: an outset channel's zero is a closed form. But it
    /// only bounds the answer: where an outset channel is 0, the Rec.2020 → Rec.709 matrix's negative off-diagonal terms take that
    /// Rec.709 channel below 0, and the 2.2 power between them has no closed form. So the bound is the top of a bisection
    /// (<see cref="SaturationBisections"/> halvings) through the rest of the curve, which keeps the side that does not clip.</para>
    /// </summary>
    private static Vector3 GamutSafePunchy(Vector3 sigmoid, float margin, out float saturation)
    {
        saturation = PunchySaturation;
        Vector3 display = AgxDisplay(AgxPunchy(sigmoid, saturation), margin, out bool clipped);
        if (!clipped)
            return display;

        float luma = Luma(sigmoid);
        Vector3 offset = AgxOutset.Times(new Vector3(luma));
        Vector3 slope = AgxOutset.Times(Power(sigmoid) - new Vector3(luma));
        float high = PunchySaturation;
        for (int k = 0; k < 3; k++)
        {
            if (slope[k] < 0f && offset[k] + high * slope[k] < 0f)
                high = Math.Min(high, -offset[k] / slope[k]);
        }

        saturation = 1f;
        display = AgxDisplay(AgxPunchy(sigmoid, saturation), margin, out clipped);
        if (clipped || high <= saturation)
            return display;
        for (int i = 0; i < SaturationBisections; i++)
        {
            float middle = 0.5f * (saturation + high);
            Vector3 trial = AgxDisplay(AgxPunchy(sigmoid, middle), margin, out clipped);
            if (clipped)
            {
                high = middle;
            }
            else
            {
                (saturation, display) = (middle, trial);
            }
        }
        return display;
    }

    /// <summary>
    /// The rest of AgX after the sigmoid and the look: the outset, a 2.2 power back to linear, back to Rec.709, not yet clamped.
    /// <paramref name="clipped"/> says whether a channel went below 0, at the outset (where the power's clamp cuts it) or in Rec.709,
    /// or in Rec.709 below <paramref name="margin"/> times the colour's luminance (<see cref="DisplayGrade.SkyMargin"/>).
    /// </summary>
    private static Vector3 AgxDisplay(Vector3 graded, float margin, out bool clipped)
    {
        Vector3 c = AgxOutset.Times(graded);
        clipped = Math.Min(c.X, Math.Min(c.Y, c.Z)) < 0f;
        c = new Vector3(MathF.Pow(Math.Max(c.X, 0f), 2.2f), MathF.Pow(Math.Max(c.Y, 0f), 2.2f), MathF.Pow(Math.Max(c.Z, 0f), 2.2f));
        c = Rec2020ToRec709.Times(c);
        float floor = margin > 0f ? margin * Math.Max(Vector3.Dot(c, new Vector3(0.2126f, 0.7152f, 0.0722f)), 0f) : 0f;
        clipped |= Math.Min(c.X, Math.Min(c.Y, c.Z)) < floor;
        return c;
    }

    /// <summary>
    /// AgX's punchy look on the sigmoid's output, ported from Filament's <c>agxLook</c> (<c>filament/src/ToneMapper.cpp</c>, Apache 2.0;
    /// Wrensch's after Blender's): an ASC CDL power of 1.35 per channel (slope 1, offset 0), then the saturation about the luma
    /// before the power (Rec.709's weights, as Filament's) scaled by <paramref name="saturation"/> (Filament's 1.4,
    /// <see cref="PunchySaturation"/>, unless that clips: <see cref="GamutSafePunchy"/>). One change: the channel is clamped at 0
    /// before the power. Filament's own sigmoid fit never goes below 0.0028; three.js's (Wrensch's) dips to −0.0023 at the bottom of
    /// the encoding, where the power would be NaN.
    /// </summary>
    internal static Vector3 AgxPunchy(Vector3 sigmoid, float saturation)
    {
        float luma = Luma(sigmoid);
        return new Vector3(luma) + saturation * (Power(sigmoid) - new Vector3(luma));
    }

    /// <summary>The punchy look's luma: Rec.709's weights on the sigmoid's output, before the power (as Filament's).</summary>
    private static float Luma(Vector3 sigmoid) => Vector3.Dot(sigmoid, new Vector3(0.2126f, 0.7152f, 0.0722f));

    /// <summary>The punchy look's power, each channel clamped at 0 first.</summary>
    private static Vector3 Power(Vector3 sigmoid) =>
        new(MathF.Pow(Math.Max(sigmoid.X, 0f), PunchyPower), MathF.Pow(Math.Max(sigmoid.Y, 0f), PunchyPower), MathF.Pow(Math.Max(sigmoid.Z, 0f), PunchyPower));

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
    public static byte EncodeSrgb(float linear) => Quantise8(Transfer(linear));

    /// <summary>The sRGB transfer of a linear value, clamped to [0, 1]; NaN maps to 0.</summary>
    private static float Transfer(float linear)
    {
        if (float.IsNaN(linear) || linear <= 0f)
            return 0f;
        if (linear >= 1f)
            return 1f;
        return linear <= 0.0031308f ? 12.92f * linear : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
    }

    /// <summary>An sRGB-encoded value on [0, 1] rounded to the nearest 8-bit step.</summary>
    private static byte Quantise8(float encoded) => (byte)(encoded * 255f + 0.5f);

    /// <summary>
    /// An sRGB-encoded value on [0, 1] quantised to 8 bits with <see cref="DitherOffset"/> added first. Black and white (the curve's
    /// clamp, or a value it never reaches) stay exact, so a clipped sky or a black frame is not speckled.
    /// </summary>
    private static byte Dithered8(float encoded, int x, int y, int channel)
    {
        if (encoded <= 0f)
            return 0;
        if (encoded >= 1f)
            return 255;
        return (byte)Math.Clamp((int)MathF.Floor(encoded * 255f + 0.5f + DitherOffset(x, y, channel)), 0, 255);
    }

    /// <summary>The dither's stream (<see cref="StableHash"/>), apart from every world's.</summary>
    private static readonly ulong DitherSeed = StableHash.Of("png.dither");

    /// <summary>
    /// The dither of frame pixel (<paramref name="x"/>, <paramref name="y"/>) in <paramref name="channel"/>, in 8-bit steps: the
    /// difference of two uniforms on [0, 1) from one hash of the pixel and the channel, so triangular on (−1, 1) and zero-mean
    /// (TPDF). Each uniform takes 24 of the hash's bits, which a float holds exactly.
    /// </summary>
    internal static float DitherOffset(int x, int y, int channel)
    {
        ulong h = StableHash.Lattice(DitherSeed + (ulong)channel, x, y);
        const float unit = 1f / (1 << 24);
        return (h & 0xFFFFFF) * unit - ((h >> 32) & 0xFFFFFF) * unit;
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
