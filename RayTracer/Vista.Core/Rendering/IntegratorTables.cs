using System.Numerics;
using Vista.Scene;

namespace Vista.Rendering;

/// <summary>
/// The spectral tables the integrator uploads besides the atmosphere's (vista-plan §5.1 / §6.4), one entry per
/// hero wavelength of a <see cref="WavelengthTable"/> (50 wavelengths; Vista's own bins since M1.12a, or the engine's). The
/// integrator samples hero wavelengths as the maze does: hero index <c>(pixelHash + sampleIndex) mod 50</c>
/// plus <see cref="CompanionCount"/> − 1 companions at a stride of <see cref="CompanionStride"/>, so 50 consecutive
/// samples of a pixel visit every wavelength <see cref="CompanionCount"/> times. Surfaces are not dispersive, so
/// all companions share the hero's path and the throughput is a float4 (the reference atmosphere is the
/// exception, and carries the hero alone). The light itself is <see cref="Atmosphere.AtmosphereTables"/>.
///
/// <para>Layouts (all <c>float</c>, row-major; the HLSL reads them as <c>StructuredBuffer</c>s):</para>
/// <list type="bullet">
///   <item><see cref="Cie"/>: 4 per entry — the table's tristimulus weights (<see cref="WavelengthTable.Cie"/>)
///   and the wavelength in nm, the engine's <c>DeterXYZ</c> layout.</item>
///   <item><see cref="Reflectance"/>: <c>[material · Count + entry]</c>, the built-in-tier spectrum of each
///   material: its own (<see cref="VistaMaterial.Spectrum"/>, M1.12), else <see cref="ReflectanceBasis"/> applied to its
///   linear-sRGB albedo.</item>
///   <item><see cref="Albedo"/>: 4 per material — its linear-sRGB albedo, for the albedo output (a spectrum's colour through the
///   table, <see cref="WavelengthTable.LinearRgbOf"/>), and its roughness (<see cref="VistaMaterial.Roughness"/>, M1.12).</item>
/// </list>
/// A pixel-sample's XYZ is <c>Σₖ Cie[iₖ].xyz · L(λᵢₖ) / CompanionCount · DeterministicCorrection</c>, which
/// maps a flat spectrum of 1 to luminance 1: Y is the ȳ-weighted mean spectral radiance, in W·m⁻²·sr⁻¹·nm⁻¹, and
/// <see cref="LuminancePerY"/> times it is luminance in cd/m².
/// </summary>
public sealed class IntegratorTables
{
    /// <summary>Wavelengths traced together per sample (the hero plus three companions, as in the engine).</summary>
    public const int CompanionCount = 4;

    /// <summary>
    /// Luminance in cd/m² per unit of Y: 683 lm/W times the integral of the CIE 1931 ȳ over 360–830 nm in 1 nm
    /// steps (106.857 nm, from the engine's own table), since Y is the ȳ-weighted mean spectral radiance.
    /// </summary>
    public const double LuminancePerY = 683.0 * 106.857;

    private IntegratorTables(WavelengthTable wavelengths, int count, float correction, float[] cie, float[] reflectance, float[] albedo)
    {
        Wavelengths = wavelengths;
        Count = count;
        DeterministicCorrection = correction;
        Cie = cie;
        Reflectance = reflectance;
        Albedo = albedo;
    }

    /// <summary>The wavelength table the tables are laid out on.</summary>
    public WavelengthTable Wavelengths { get; }

    /// <summary>Hero wavelengths in the cycle (50).</summary>
    public int Count { get; }

    /// <summary>Stride between companion wavelength indices.</summary>
    public int CompanionStride => Count / CompanionCount;

    /// <summary>Normalises a flat-white spectrum to luminance 1 through the hero sum.</summary>
    public float DeterministicCorrection { get; }

    /// <summary>Tristimulus weight and wavelength, 4 floats per entry.</summary>
    public float[] Cie { get; }

    /// <summary>Reflectance per material per entry.</summary>
    public float[] Reflectance { get; }

    /// <summary>Linear-sRGB albedo per material, 4 floats per material.</summary>
    public float[] Albedo { get; }

    /// <summary>The wavelength in nm of entry <paramref name="index"/>.</summary>
    public float WavelengthNm(int index) => Cie[index * 4 + 3];

    /// <summary>
    /// Builds the tables for <paramref name="materials"/> (indexed by <see cref="VistaMaterial.Id"/>) on
    /// <paramref name="wavelengths"/>. Reflectance is clamped to [0, 1]: the basis is a minimum-norm fit that can dip below
    /// zero for saturated colours, which a multi-bounce path must never see. A material with a spectrum gets its colour
    /// through this table as its albedo, so the albedo output shows what it renders with.
    /// </summary>
    public static IntegratorTables Build(IReadOnlyList<VistaMaterial> materials, WavelengthTable wavelengths)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(wavelengths);
        for (int m = 0; m < materials.Count; m++)
        {
            if (materials[m].Id != m)
                throw new ArgumentException($"Material ids must equal their index; material {m} has id {materials[m].Id}.", nameof(materials));
        }

        int n = WavelengthTable.Count;
        ReflectanceBasis basis = ReflectanceBasis.Build(wavelengths);

        var cie = (float[])wavelengths.Cie.Clone();
        var reflectance = new float[materials.Count * n];
        var albedo = new float[materials.Count * 4];
        for (int m = 0; m < materials.Count; m++)
        {
            (float r, float g, float b) = materials[m].LinearRgbAlbedo;
            if (materials[m].Spectrum is { } spectrum)
            {
                for (int i = 0; i < n; i++)
                    reflectance[m * n + i] = Math.Clamp((float)ReflectanceAt(spectrum, cie[i * 4 + 3]), 0f, 1f);
                (r, g, b) = wavelengths.LinearRgbOf(spectrum);
            }
            else
            {
                float[] curve = basis.Reflectance(new Vector3(r, g, b));
                for (int i = 0; i < n; i++)
                    reflectance[m * n + i] = Math.Clamp(curve[i], 0f, 1f);
            }
            albedo[m * 4] = r;
            albedo[m * 4 + 1] = g;
            albedo[m * 4 + 2] = b;
            albedo[m * 4 + 3] = materials[m].Roughness;
        }

        return new IntegratorTables(wavelengths, n, wavelengths.DeterministicCorrection, cie, reflectance, albedo);
    }

    /// <summary>
    /// The reflectance of a spectrum given as (wavelength in nm, reflectance) pairs at <paramref name="nm"/>: linear between its
    /// points, flat past its ends (<see cref="VistaMaterial.Spectrum"/>).
    /// </summary>
    public static double ReflectanceAt(IReadOnlyList<double> spectrum, double nm)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        if (spectrum.Count < 2 || spectrum.Count % 2 != 0)
            throw new ArgumentException("A spectrum is (wavelength, reflectance) pairs, at least one.", nameof(spectrum));
        if (nm <= spectrum[0])
            return spectrum[1];
        for (int k = 2; k < spectrum.Count; k += 2)
        {
            if (nm <= spectrum[k])
                return spectrum[k - 1] + (spectrum[k + 1] - spectrum[k - 1]) * (nm - spectrum[k - 2]) / (spectrum[k] - spectrum[k - 2]);
        }
        return spectrum[^1];
    }

    /// <summary>
    /// The XYZ one pixel-sample contributes when its companion wavelengths, starting at hero index
    /// <paramref name="heroIndex"/>, carry <paramref name="radiance"/> — the C# reference of the shader's
    /// final step.
    /// </summary>
    public Vector3 SampleXyz(int heroIndex, ReadOnlySpan<float> radiance)
    {
        if (radiance.Length != CompanionCount)
            throw new ArgumentException($"Expected {CompanionCount} radiance values.", nameof(radiance));
        Vector3 xyz = Vector3.Zero;
        for (int k = 0; k < CompanionCount; k++)
        {
            int i = (heroIndex + k * CompanionStride) % Count;
            xyz += new Vector3(Cie[i * 4], Cie[i * 4 + 1], Cie[i * 4 + 2]) * radiance[k];
        }
        return xyz / CompanionCount * DeterministicCorrection;
    }
}
