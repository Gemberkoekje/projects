namespace RayTracer;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;

public class WavelengthLookup
{
    private readonly FrozenDictionary<int, Vector3> _lookup;
    // removed unused frozen dictionaries for random sampling (mechanical cleanup)
    private readonly int[] _deter;
    private readonly FrozenDictionary<int, float> _deterpdf;
    private readonly int DeterministicWaveLengths = 50;
    public readonly float DeterministicCorrection;

    public WavelengthLookup()
    {
        var lookup = new Dictionary<int, Vector3>();
        var randompdf = new Dictionary<int, float>();
        var randomdeter = new Dictionary<(float Min, float Max), int>();
        var deterpdf = new Dictionary<int, float>();
        var deter = new List<int>();
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "RayTracer.Data.CIE_xyz_1931_2deg.csv";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using (var reader = new StreamReader(stream))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length != 4) continue;

                if (int.TryParse(parts[0], out int wavelength) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    lookup[wavelength] = new Vector3(x, y, z);
                }
            }
        }
        var total = lookup.Values.Sum(v => v.X + v.Y + v.Z);
        var sum = 0f;
        foreach (var key in lookup.Keys.ToList())
        {
            randompdf[key] = ((lookup[key].X + lookup[key].Y + lookup[key].Z) / total);
            var min = sum;
            var max = sum + ((lookup[key].X + lookup[key].Y + lookup[key].Z) / total);
            sum = max;
            randomdeter[(min, max)] = key;
        }
        var step = 1f / ((float)DeterministicWaveLengths + 1f);
        for (int i = 1; i <= DeterministicWaveLengths; i++)
        {
            var cur = i * step;
            var hero = randomdeter.FirstOrDefault(r => r.Key.Min < cur && r.Key.Max > cur).Value;
            deter.Add(hero);
            deterpdf[hero] = step;
        }
        _deter = deter.ToArray();
        _deterpdf = deterpdf.ToFrozenDictionary();

        // White-balance the CIE table (the fix for the coarse hero integration's magenta cast). The
        // deterministic hero set is importance-sampled ∝ (x̄+ȳ+z̄), so heroes cluster where that sum is
        // large (the blue, then the red) and thin out in the green. Accumulating XYZ(hero)·radiance(hero)
        // with a *uniform* per-hero weight — as every path does — is therefore a biased quadrature: it
        // over-weights the dense blue/red and under-weights the green, rendering a spectrally-flat colour
        // (a white wall, the hazy sky) with a faint magenta tint. The unbiased weight each hero must carry
        // is the width of its CDF bin, ∝ 1/(x̄+ȳ+z̄). Folding that weight straight into the CIE table makes
        // <see cref="TryGet"/> — and hence every downstream consumer that derives from it (the GPU
        // DeterXYZ buffer, the C# phase references, the RGB→reflectance basis, the CPU path tracer) — an
        // unbiased tristimulus integrator in one place, so an equal-energy spectrum resolves to a neutral
        // grey. Normalised so the mean hero weight is 1 (overall luminance scale unchanged); the exact
        // luminance is then pinned by DeterministicCorrection below.
        float meanSum = 0f;
        foreach (var wl in _deter)
            if (lookup.TryGetValue(wl, out var raw))
                meanSum += raw.X + raw.Y + raw.Z;
        meanSum /= _deter.Length;

        var weighted = new Dictionary<int, Vector3>(lookup.Count);
        foreach (var (wl, xyz) in lookup)
        {
            float chromaSum = xyz.X + xyz.Y + xyz.Z;
            weighted[wl] = chromaSum > 1e-6f ? xyz * (meanSum / chromaSum) : Vector3.Zero;
        }
        _lookup = weighted.ToFrozenDictionary();

        // DeterministicCorrection normalises a flat-white spectrum to luminance 1 through the (now
        // white-balanced) hero sum, so it must be computed from the weighted table.
        var totald = new Vector3(0, 0, 0);
        foreach (var wl in _deter)
            if (_lookup.TryGetValue(wl, out var xyz))
                totald += xyz;
        var average = totald / new Vector3(_deter.Length, _deter.Length, _deter.Length);
        DeterministicCorrection = 1 / average.Y;
    }

    /// <summary>Number of wavelengths in the deterministic cycle.</summary>
    public int DeterministicCount => DeterministicWaveLengths;

    public int GetHeroWavelength(uint pixelid, long samplecount)
    {
        return _deter[(int)((pixelid + samplecount) % DeterministicWaveLengths)];
    }

    /// <summary>
    /// Returns the deterministic wavelength at the given index (mod cycle length).
    /// Used for companion wavelength evaluation.
    /// </summary>
    public int GetDeterministicWavelength(int index)
    {
        return _deter[((index % DeterministicWaveLengths) + DeterministicWaveLengths) % DeterministicWaveLengths];
    }

    public float GetHeroWavelengthProbability(int heroWavelength)
    {
        return _deterpdf[heroWavelength];
    }

    /// <summary>The white-balanced CIE XYZ response at <paramref name="wavelength"/> — the raw CIE 1931
    /// curve scaled by the hero-set quadrature weight (∝ 1/(x̄+ȳ+z̄); see the constructor). Multiplying
    /// this by a hero wavelength's radiance and summing over the deterministic cycle yields an unbiased
    /// tristimulus, so an equal-energy spectrum resolves neutral. Every XYZ-accumulating consumer uses
    /// this (directly or via the baked DeterXYZ table).</summary>
    public bool TryGet(int wavelength, out Vector3 xyz)
    {
        return _lookup.TryGetValue(wavelength, out xyz);
    }

    public Vector3 this[int wavelength] => _lookup[wavelength];
}
