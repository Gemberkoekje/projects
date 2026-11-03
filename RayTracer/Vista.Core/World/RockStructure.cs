namespace Vista.World;

/// <summary>
/// The rock's structure at 4–64 m (vista-plan §10 M1a.4): the gullies and ribs that run down a rock face's fall line, and the ledges
/// and cliff bands its beds' hardness makes, where the world grid's faces were smooth planes. Both are a height added to bare rock,
/// a pure function of position (and of the ground's height, for the ledges); the view-dependent mesh carries their coarse part as
/// geometry (<see cref="MaterialDetail"/>), for outlines and the shadows a low sun casts, and the shader shades all of it from the
/// same functions (<c>RockGullies</c> and <c>RockLedges</c> in <c>VistaMaterials.hlsli</c>, operation for operation in single
/// precision, hashed with the integrator's PCG hash), so the mesh's normals leave it out and the shading is the same at every distance.
///
/// <para><b>Gullies and ribs.</b> Each octave is phasor noise (Tricard et al. 2019): from a jittered point in each of the 3×3 cells
/// around a point, a plane wave of the octave's wavelength across the fall line, each with its own phase, weighted by a radial kernel
/// and summed as complex numbers. The sum's angle is a stripe pattern of full contrast that runs down the slope, whose stripes start,
/// end and fork where the waves cancel (there the sum's magnitude falls and the octave fades out). The angle is shaped into a rib
/// (rounded over <see cref="RibRounding"/>) and a rounded gully floor between. Each finer octave runs down the fall line of the grid
/// and the coarser octaves together, so small gullies run down the sides of large ribs and join the large gullies below them: they
/// branch up the slope. The depth is <c>depth·λ</c> across the face, so the vertical height is that over the slope's cosine (at most
/// <see cref="SecMax"/> times it); it fades in from <see cref="SlopeFrom"/> to <see cref="SlopeTo"/> (25° to 40°).</para>
///
/// <para><b>Ledges.</b> Rock harder than the beds around it (within <see cref="LedgeTaps"/> entries of the bed table, 8 m) stands
/// steeper and softer rock recedes: the slope along any direction is times <c>1 + G'</c> with <c>G' = gain·(H − H̄)</c>, so a contact
/// between a hard bed and a soft one becomes a cliff band over a bench, and thin beds a staircase. <c>G</c> is its integral over the
/// stratigraphic height, a table per bed-table entry (<see cref="LedgeTable"/>), high-passed so it cannot drift; a seeded variation
/// within the beds (<see cref="SubBedContrast"/> at <see cref="SubBedMetres"/>) breaks thick beds into ledges too. Both sides read it
/// as a Catmull–Rom spline, so its slope is continuous. Massive rock has no ledges.</para>
/// </summary>
public static class RockStructure
{
    /// <summary>A gully octave's cell, in its wavelengths: each cell holds a stripe pattern about this many wavelengths long.</summary>
    public const float Cell = 2f;

    /// <summary>
    /// A kernel's radius, in cells: the jittered points lie at least 0.15 of a cell inside their cells, so every point within 1.15 cells
    /// lies in the 3×3 cells around the one a point is in, and the sum misses none.
    /// </summary>
    public const float KernelRadius = 1.1f;

    /// <summary>How far a rib's crest is rounded, in radians of the stripes' phase (of π from rib to floor).</summary>
    public const float RibRounding = 0.3f;

    /// <summary>The slope (as a gradient) the gullies start at: tan 25°.</summary>
    public const float SlopeFrom = 0.47f;

    /// <summary>The slope the gullies reach their full depth at: tan 40°.</summary>
    public const float SlopeTo = 0.84f;

    /// <summary>The largest factor from a gully's depth across the face to its height (the slope's secant, held at 76°).</summary>
    public const float SecMax = 4f;

    /// <summary>The phasor's magnitude over its kernels' sum below which an octave has faded out.</summary>
    public const float ContrastFrom = 0.1f;

    /// <summary>The phasor's magnitude over its kernels' sum above which an octave is at its full depth.</summary>
    public const float ContrastTo = 0.45f;

    /// <summary>The bed-table entries either side the ledges compare a bed's hardness with (triangular weights).</summary>
    public const int LedgeTaps = 4;

    /// <summary>The entries either side whose mean the ledges' height is high-passed by, so it never drifts.</summary>
    public const int LedgeHighPass = 32;

    /// <summary>The hardness the seeded variation within the beds adds at most.</summary>
    public const float SubBedContrast = 0.12f;

    /// <summary>The wavelength of the variation within the beds, m of stratigraphic height.</summary>
    public const float SubBedMetres = 9f;

    /// <summary>The largest |profile| a rib-and-gully octave reaches, in units of its depth: the floor's 2/3 below and the rib's 4/3 above its mean.</summary>
    public const float ProfileMax = 4f / 3f;

    private static readonly float RoundedPi = MathF.Sqrt(MathF.PI * MathF.PI + RibRounding * RibRounding) - RibRounding;

    /// <summary>
    /// The measured envelope of <see cref="Unit"/>: the 99.9th percentile of its midpoint deviation over 200 k random segments at each
    /// length, ×1.15 (the largest are 3–4 times it below a tenth of a wavelength, where an octave's phase turns fast as it fades out
    /// at a fork, and the mesh holds the structure loosely anyway); below its first length the curvature bound with the rib's
    /// curvature, 210 in lattice units. A test checks it on fresh segments.
    /// </summary>
    public static DetailEnvelope Envelope { get; } = new(
    [
        0.00390625, 0.0004, 0.0078125, 0.0016, 0.015625, 0.0062, 0.03125, 0.0239, 0.0625, 0.0908, 0.125, 0.2906, 0.25, 0.7194, 0.5, 1.3530,
        1.0, 1.7200,
    ], 210.0);

    /// <summary>
    /// One gully octave in lattice units, for its bound: wavelength 1, as deep as makes its largest height 1, down a slope steeper than
    /// <see cref="SecMax"/>'s in a direction <paramref name="seed"/> chooses.
    /// </summary>
    public static double Unit(ulong seed, double x, double y)
    {
        double a = (seed >> 11) * (2.0 * Math.PI / (1ul << 53));
        return Gullies((uint)seed, (float)x, (float)y, (float)(5.0 * Math.Cos(a)), (float)(5.0 * Math.Sin(a)), 1f / (SecMax * ProfileMax), 1f, 1).Height;
    }

    /// <summary>
    /// The gullies and ribs at world <c>(x, z)</c> on ground whose height gradient is <c>(gx, gz)</c> (the world grid's): their height,
    /// m, its gradient, and how far the point lies in a gully's floor (0 on a rib, 1 at the floor; by the octaves' depths). The first
    /// <paramref name="octaves"/> octaves from <paramref name="coarsest"/> m, each <paramref name="depth"/> of its wavelength deep; with
    /// a pixel covering <paramref name="footprint"/> metres along the ground (0: none, every octave whole) an octave fades out between
    /// four and two pixels a wavelength. <c>RockGullies</c> in <c>VistaMaterials.hlsli</c> is its twin.
    /// </summary>
    public static (float Height, float Gx, float Gz, float Floor) Gullies(uint seed, float x, float z, float gx, float gz, float depth, float coarsest,
        int octaves, float footprint = 0f)
    {
        float steep = MathF.Sqrt(gx * gx + gz * gz);
        float slopeFade = SmoothStep(SlopeFrom, SlopeTo, steep);
        if (slopeFade <= 0f || depth <= 0f)
            return (0f, 0f, 0f, 0f);
        float sec = MathF.Min(MathF.Sqrt(1f + steep * steep), SecMax);
        float ax = gx, az = gz;
        float height = 0f, addedX = 0f, addedZ = 0f, floor = 0f, depthSum = 0f;
        float lambda = coarsest;
        for (int k = 0; k < octaves; k++, lambda *= 0.5f)
        {
            float fade = footprint > 0f ? SmoothStep(2f * footprint, 4f * footprint, lambda) : 1f;
            if (fade <= 0f)
                break;
            float len = MathF.Sqrt(ax * ax + az * az);
            if (len < 1e-4f)
                break;
            float acrossX = -az / len, acrossZ = ax / len;
            uint octaveSeed = MaterialNoise.Hash(seed ^ (0x6A11u + (uint)k));
            float omega = 6.2831853f / lambda;
            Phasor(octaveSeed, x, z, omega, Cell * lambda, acrossX, acrossZ, out float zRe, out float zIm, out float reX, out float reZ, out float imX,
                out float imZ, out float weights);
            float m2 = zRe * zRe + zIm * zIm;
            if (m2 < 1e-12f || weights <= 0f)
                continue;
            float magnitude = MathF.Sqrt(m2);
            float contrast = SmoothStep(ContrastFrom, ContrastTo, magnitude / weights);
            float phi = MathF.Atan2(zIm, zRe);
            float phiX = (zRe * imX - zIm * reX) / m2, phiZ = (zRe * imZ - zIm * reZ) / m2;
            (float profile, float profileSlope, float t) = Profile(phi);
            float amplitude = depth * lambda * sec * slopeFade * contrast * fade;
            float dx = amplitude * profileSlope * phiX, dz = amplitude * profileSlope * phiZ;
            height += amplitude * profile;
            addedX += dx;
            addedZ += dz;
            ax += dx;
            az += dz;
            float d = depth * lambda * contrast * fade;
            floor += d * (1f - t) * (1f - t);
            depthSum += d;
        }
        return (height, addedX, addedZ, depthSum > 0f ? floor / depthSum : 0f);
    }

    /// <summary>
    /// The profile across a stripe of phase <paramref name="phi"/> (−π to π): <c>2t² − 2/3</c> with <c>t = 1 − u/π</c> and <c>u</c>
    /// the rounded |φ|, a rib at φ = 0 and a rounded floor at ±π, its mean about 0; its slope by φ; and t.
    /// </summary>
    internal static (float Profile, float Slope, float T) Profile(float phi)
    {
        float root = MathF.Sqrt(phi * phi + RibRounding * RibRounding);
        float u = (root - RibRounding) * (MathF.PI / RoundedPi);
        float t = 1f - u * (1f / MathF.PI);
        float du = phi / root * (MathF.PI / RoundedPi);
        return (2f * t * t - 2f / 3f, -4f * t * (1f / MathF.PI) * du, t);
    }

    /// <summary>
    /// One octave's phasor at world <c>(x, z)</c>: stripes of angular frequency <paramref name="omega"/> across the fall line (the unit
    /// vector <c>(ax, az)</c>), from a jittered point in each of the 3×3 cells (side <paramref name="cell"/>) around the point, each with
    /// its own phase and a radial kernel <see cref="KernelRadius"/> cells wide; the phasor, its real and imaginary parts' gradients and the kernels' sum.
    /// </summary>
    private static void Phasor(uint seed, float x, float z, float omega, float cell, float ax, float az, out float zRe, out float zIm, out float reX,
        out float reZ, out float imX, out float imZ, out float weights)
    {
        float fx = MathF.Floor(x / cell), fz = MathF.Floor(z / cell);
        int cx = (int)fx, cz = (int)fz;
        zRe = zIm = reX = reZ = imX = imZ = weights = 0f;
        float inv = 1f / (KernelRadius * KernelRadius * cell * cell);
        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                uint h = MaterialNoise.Hash(seed ^ MaterialNoise.Hash(unchecked((uint)(cx + i)) ^ MaterialNoise.Hash(unchecked((uint)(cz + j)))));
                float qx = (fx + i + 0.15f + 0.7f * (h & 0xFFFFu) * (1f / 65536f)) * cell;
                float qz = (fz + j + 0.15f + 0.7f * (h >> 16) * (1f / 65536f)) * cell;
                float rx = x - qx, rz = z - qz;
                float d2 = (rx * rx + rz * rz) * inv;
                if (d2 >= 1f)
                    continue;
                float k = 1f - d2;
                float w = k * k * k;
                float dw = -6f * k * k * inv;
                float phase = omega * (rx * ax + rz * az) + (MaterialNoise.Hash(h) >> 8) * (6.2831853f / 16777216f);
                (float s, float co) = MathF.SinCos(phase);
                zRe += w * co;
                zIm += w * s;
                reX += dw * rx * co - w * s * omega * ax;
                reZ += dw * rz * co - w * s * omega * az;
                imX += dw * rx * s + w * co * omega * ax;
                imZ += dw * rz * s + w * co * omega * az;
                weights += w;
            }
        }
    }

    /// <summary>
    /// The ledges' table of the beds <paramref name="beds"/> (<see cref="MaterialField.Beds"/>: four floats an entry, the hardness first),
    /// <paramref name="spacing"/> metres of stratigraphic height apart: per entry the height <c>G</c> the ledges add, m, four entries a
    /// float4 (what the GPU reads after the beds). <paramref name="gain"/> and <paramref name="max"/> make the slope's excess
    /// <c>clamp(gain·(H − H̄), ±max)</c>; <paramref name="seed"/> seeds the variation within the beds.
    /// </summary>
    public static float[] LedgeTable(ReadOnlySpan<float> beds, float spacing, float gain, float max, uint seed)
    {
        int n = beds.Length / 4;
        var hardness = new double[n];
        uint stream = MaterialNoise.Hash(seed ^ 0x1ED6u);
        for (int e = 0; e < n; e++)
            hardness[e] = beds[4 * e] + SubBedContrast * Noise1(stream, e * spacing / SubBedMetres);
        var slope = new double[n];
        for (int e = 0; e < n; e++)
        {
            double sum = 0.0, total = 0.0;
            for (int m = -LedgeTaps; m <= LedgeTaps; m++)
            {
                double w = 1.0 - Math.Abs(m) / (LedgeTaps + 1.0);
                sum += w * hardness[Math.Clamp(e + m, 0, n - 1)];
                total += w;
            }
            slope[e] = Math.Clamp(gain * (hardness[e] - sum / total), -max, max);
        }
        var height = new double[n];
        for (int e = 1; e < n; e++)
            height[e] = height[e - 1] + 0.5 * (slope[e - 1] + slope[e]) * spacing;
        double[] mean = MovingMean(height, LedgeHighPass);
        var table = new float[4 * ((n + 3) / 4)];
        for (int e = 0; e < n; e++)
            table[e] = (float)(height[e] - mean[e]);
        return table;
    }

    /// <summary>
    /// The ledges' height <c>G</c> at bed-table coordinate <paramref name="t"/> from <paramref name="table"/> (<see cref="LedgeTable"/>),
    /// m, and its slope by the coordinate (m per entry): a Catmull–Rom spline through the entries. <c>LedgeAt</c> in the shader is its twin.
    /// </summary>
    public static (float Height, float Slope) LedgeAt(ReadOnlySpan<float> table, int entries, float t)
    {
        t = Math.Clamp(t, 1f, entries - 3f);
        int e = (int)MathF.Floor(t);
        float f = t - e;
        float p0 = table[e - 1], p1 = table[e], p2 = table[e + 1], p3 = table[e + 2];
        float a = -p0 + p2, b = 2f * p0 - 5f * p1 + 4f * p2 - p3, c = -p0 + 3f * p1 - 3f * p2 + p3;
        return (0.5f * (2f * p1 + f * (a + f * (b + f * c))), 0.5f * (a + f * (2f * b + 3f * f * c)));
    }

    /// <summary>1-D gradient noise in lattice units, roughly in [−1, 1]: a hashed slope at each integer, faded between.</summary>
    internal static double Noise1(uint seed, double x)
    {
        double f = Math.Floor(x);
        long i = (long)f;
        double d = x - f;
        double g0 = MaterialNoise.Hash(seed ^ unchecked((uint)i)) * (2.0 / uint.MaxValue) - 1.0;
        double g1 = MaterialNoise.Hash(seed ^ unchecked((uint)(i + 1))) * (2.0 / uint.MaxValue) - 1.0;
        double u = d * d * d * (d * (d * 6.0 - 15.0) + 10.0);
        return 2.0 * (g0 * d + (g1 * (d - 1.0) - g0 * d) * u);
    }

    private static double[] MovingMean(double[] values, int half)
    {
        int n = values.Length;
        var prefix = new double[n + 1];
        for (int i = 0; i < n; i++)
            prefix[i + 1] = prefix[i] + values[i];
        var mean = new double[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(i - half, 0), hi = Math.Min(i + half, n - 1);
            mean[i] = (prefix[hi + 1] - prefix[lo]) / (hi - lo + 1);
        }
        return mean;
    }

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
