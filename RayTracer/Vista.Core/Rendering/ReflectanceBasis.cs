using System.Numerics;
using RayTracer;

namespace Vista.Rendering;

/// <summary>
/// Maps a linear-sRGB colour to a reflectance at each entry of a <see cref="WavelengthTable"/> (vista-plan §10 M1.12a): the engine's
/// <see cref="RgbToReflectanceBasis"/> for any table, since the engine builds its own only from its own table. Over a cycle the
/// integrator turns a reflectance vector <c>R</c> into the linear-sRGB colour <c>c = A·R</c>, column <c>i</c> of <c>A</c> being
/// <c>(correction / N) · M · Cie[i]</c> with <c>M</c> the engine's XYZ → linear-sRGB matrix; the basis is the minimum-norm right inverse
/// <c>B = Aᵀ (A Aᵀ)⁻¹</c>, so <c>A·(B·c) = c</c> and the reflectance is the smoothest one that renders as <c>c</c>. The arithmetic is
/// the engine's step for step, so on <see cref="WavelengthTable.Engine"/> the basis is the engine's to the bit.
/// </summary>
public sealed class ReflectanceBasis
{
    // XYZ → linear sRGB (sRGB / D65), the engine's matrix (RgbToReflectanceBasis).
    private static readonly float[] XyzToLinearRgb =
    [
        3.2406f, -1.5372f, -0.4986f,
        -0.9689f, 1.8758f, 0.0415f,
        0.0557f, -0.2040f, 1.0570f,
    ];

    private readonly int _n;
    private readonly float[] _basis;   // N×3, row-major: [i*3 + channel]
    private readonly float[] _forward; // 3×N, row-major: [channel*N + i] (the map A)

    private ReflectanceBasis(int n, float[] basis, float[] forward)
    {
        _n = n;
        _basis = basis;
        _forward = forward;
    }

    /// <summary>The basis, <c>[entry*3 + channel]</c>.</summary>
    public IReadOnlyList<float> Data => _basis;

    /// <summary>Reflectance at each entry for a linear-sRGB colour (<c>B·c</c>).</summary>
    public float[] Reflectance(Vector3 linearRgb)
    {
        var r = new float[_n];
        for (int i = 0; i < _n; i++)
            r[i] = _basis[i * 3 + 0] * linearRgb.X
                 + _basis[i * 3 + 1] * linearRgb.Y
                 + _basis[i * 3 + 2] * linearRgb.Z;
        return r;
    }

    /// <summary>The linear-sRGB colour a reflectance vector integrates to over a cycle (<c>A·R</c>).</summary>
    public Vector3 ForwardLinearRgb(IReadOnlyList<float> reflectance)
    {
        ArgumentNullException.ThrowIfNull(reflectance);
        float r = 0, g = 0, b = 0;
        for (int i = 0; i < _n; i++)
        {
            r += _forward[0 * _n + i] * reflectance[i];
            g += _forward[1 * _n + i] * reflectance[i];
            b += _forward[2 * _n + i] * reflectance[i];
        }
        return new Vector3(r, g, b);
    }

    /// <summary>Builds the basis for <paramref name="wavelengths"/>.</summary>
    public static ReflectanceBasis Build(WavelengthTable wavelengths)
    {
        ArgumentNullException.ThrowIfNull(wavelengths);
        int n = WavelengthTable.Count;
        float scale = wavelengths.DeterministicCorrection / n;

        // A (3×N): column i is (correction/N) · M · Cie[i].
        var a = new float[3 * n];
        for (int i = 0; i < n; i++)
        {
            float x = wavelengths.Cie[i * 4 + 0];
            float y = wavelengths.Cie[i * 4 + 1];
            float z = wavelengths.Cie[i * 4 + 2];
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
        {
            for (int c = 0; c < 3; c++)
            {
                float s = 0;
                for (int i = 0; i < n; i++)
                    s += a[r * n + i] * a[c * n + i];
                aat[r * 3 + c] = s;
            }
        }

        float[] inv = Invert3x3(aat);

        // B = Aᵀ (AAᵀ)⁻¹ → N×3, row-major [i*3 + c].
        var b = new float[n * 3];
        for (int i = 0; i < n; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                float s = 0;
                for (int k = 0; k < 3; k++)
                    s += a[k * n + i] * inv[k * 3 + c];
                b[i * 3 + c] = s;
            }
        }

        return new ReflectanceBasis(n, b, a);
    }

    private static float[] Invert3x3(float[] m)
    {
        float a = m[0], b = m[1], c = m[2];
        float d = m[3], e = m[4], f = m[5];
        float g = m[6], h = m[7], i = m[8];

        float det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (MathF.Abs(det) < 1e-20f)
            throw new InvalidOperationException("The XYZ response matrix is singular; cannot build the reflectance basis.");
        float invDet = 1f / det;

        return
        [
            (e * i - f * h) * invDet, (c * h - b * i) * invDet, (b * f - c * e) * invDet,
            (f * g - d * i) * invDet, (a * i - c * g) * invDet, (c * d - a * f) * invDet,
            (d * h - e * g) * invDet, (b * g - a * h) * invDet, (a * e - b * d) * invDet,
        ];
    }
}
