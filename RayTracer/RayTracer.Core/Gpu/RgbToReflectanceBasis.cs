using System.Numerics;

namespace RayTracer;

/// <summary>
/// Maps a linear-sRGB colour to a per-hero-wavelength reflectance curve, so image-based
/// decals (rat, OpenGL logo, wall signs — plan §3) can be shaded through the <b>same</b>
/// spectral pipeline as the maze materials instead of a bolted-on RGB path.
///
/// <para>The fullbright spectral path, in expectation over the hero-wavelength cycle,
/// integrates a reflectance vector <c>R</c> (one entry per deterministic wavelength) to a
/// linear-sRGB colour by a fixed linear map <c>c = A·R</c>, where column <c>i</c> of the
/// 3×N matrix <c>A</c> is <c>(correction / N) · M · DeterXYZ[i]</c> and <c>M</c> is the
/// XYZ→linear-sRGB matrix (see <see cref="Phase1Reference.ResolveToSRGB"/>). This class
/// builds the least-squares minimum-norm right inverse <c>B = Aᵀ (A Aᵀ)⁻¹</c> (N×3), so
/// <c>R = B·c</c> and <c>A·(B·c) = c</c> exactly: a decal colour round-trips through the
/// spectral pipeline. The min-norm solution also yields a smooth, physically-plausible
/// reflectance curve. Occasional out-of-[0,1] reflectance is harmless for decorative
/// decals (the resolve clamps the final colour).</para>
/// </summary>
public sealed class RgbToReflectanceBasis
{
    // XYZ → linear sRGB (sRGB / D65), identical to Phase1Reference.ResolveToSRGB's matrix.
    private static readonly float[] XyzToLinearRgb =
    [
        3.2406f, -1.5372f, -0.4986f,
        -0.9689f, 1.8758f, 0.0415f,
        0.0557f, -0.2040f, 1.0570f,
    ];

    private readonly int _n;
    private readonly float[] _basis;   // N×3, row-major: [i*3 + channel]
    private readonly float[] _forward; // 3×N, row-major: [channel*N + i]  (the map A)

    private RgbToReflectanceBasis(int n, float[] basis, float[] forward)
    {
        _n = n;
        _basis = basis;
        _forward = forward;
    }

    /// <summary>Number of deterministic hero wavelengths (basis rows).</summary>
    public int Count => _n;

    /// <summary>
    /// The basis as a flat <c>[wavelength*3 + channel]</c> array, ready to upload as a GPU
    /// structured buffer. The shader recovers reflectance as
    /// <c>refl[i] = dot(Basis[i], linearRgb)</c>.
    /// </summary>
    public float[] Data => _basis;

    /// <summary>Reflectance at each hero wavelength for a linear-sRGB colour (<c>B·c</c>).</summary>
    public float[] Reflectance(Vector3 linearRgb)
    {
        var r = new float[_n];
        for (int i = 0; i < _n; i++)
            r[i] = _basis[i * 3 + 0] * linearRgb.X
                 + _basis[i * 3 + 1] * linearRgb.Y
                 + _basis[i * 3 + 2] * linearRgb.Z;
        return r;
    }

    /// <summary>
    /// The linear-sRGB colour a reflectance vector integrates to (<c>A·R</c>) — the forward
    /// map the spectral pipeline realises in expectation. Exposed for parity testing.
    /// </summary>
    public Vector3 ForwardLinearRgb(float[] reflectance)
    {
        float r = 0, g = 0, b = 0;
        for (int i = 0; i < _n; i++)
        {
            r += _forward[0 * _n + i] * reflectance[i];
            g += _forward[1 * _n + i] * reflectance[i];
            b += _forward[2 * _n + i] * reflectance[i];
        }
        return new Vector3(r, g, b);
    }

    /// <summary>Builds the basis from the scene's baked spectral tables.</summary>
    public static RgbToReflectanceBasis Build(SpectralResources spectral)
    {
        ArgumentNullException.ThrowIfNull(spectral);
        int n = spectral.DeterministicCount;
        float scale = spectral.DeterministicCorrection / n;

        // A (3×N): column i is (correction/N) · M · DeterXYZ[i].
        var a = new float[3 * n];
        for (int i = 0; i < n; i++)
        {
            float x = spectral.DeterXYZ[i * 4 + 0];
            float y = spectral.DeterXYZ[i * 4 + 1];
            float z = spectral.DeterXYZ[i * 4 + 2];
            for (int c = 0; c < 3; c++)
            {
                float m0 = XyzToLinearRgb[c * 3 + 0];
                float m1 = XyzToLinearRgb[c * 3 + 1];
                float m2 = XyzToLinearRgb[c * 3 + 2];
                a[c * n + i] = scale * (m0 * x + m1 * y + m2 * z);
            }
        }

        // AAᵀ (3×3).
        var aat = new float[9];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                float s = 0;
                for (int i = 0; i < n; i++)
                    s += a[r * n + i] * a[c * n + i];
                aat[r * 3 + c] = s;
            }

        float[] inv = Invert3x3(aat);

        // B = Aᵀ (AAᵀ)⁻¹  →  N×3, row-major [i*3 + c].
        var b = new float[n * 3];
        for (int i = 0; i < n; i++)
            for (int c = 0; c < 3; c++)
            {
                float s = 0;
                for (int k = 0; k < 3; k++)
                    s += a[k * n + i] * inv[k * 3 + c];
                b[i * 3 + c] = s;
            }

        return new RgbToReflectanceBasis(n, b, a);
    }

    private static float[] Invert3x3(float[] m)
    {
        float a = m[0], b = m[1], c = m[2];
        float d = m[3], e = m[4], f = m[5];
        float g = m[6], h = m[7], i = m[8];

        float det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (MathF.Abs(det) < 1e-20f)
            throw new InvalidOperationException("XYZ response matrix is singular; cannot build reflectance basis.");
        float invDet = 1f / det;

        return
        [
            (e * i - f * h) * invDet, (c * h - b * i) * invDet, (b * f - c * e) * invDet,
            (f * g - d * i) * invDet, (a * i - c * g) * invDet, (c * d - a * f) * invDet,
            (d * h - e * g) * invDet, (b * g - a * h) * invDet, (a * e - b * d) * invDet,
        ];
    }
}
