using System.Numerics;
using RayTracer;
using Vista.Scene;

namespace Vista.Rendering;

/// <summary>
/// The spectral tables the integrator uploads besides the atmosphere's (vista-plan §5.1 / §6.4), one entry per
/// hero wavelength of the engine's deterministic cycle (<see cref="WavelengthLookup"/>, 50 wavelengths). The
/// integrator samples hero wavelengths exactly as the maze does: hero index <c>(pixelHash + sampleIndex) mod 50</c>
/// plus <see cref="CompanionCount"/> − 1 companions at a stride of <see cref="CompanionStride"/>, so 50 consecutive
/// samples of a pixel visit every wavelength <see cref="CompanionCount"/> times. Surfaces are not dispersive, so
/// all companions share the hero's path and the throughput is a float4 (the reference atmosphere is the
/// exception, and carries the hero alone). The light itself is <see cref="Atmosphere.AtmosphereTables"/>.
///
/// <para>Layouts (all <c>float</c>, row-major; the HLSL reads them as <c>StructuredBuffer</c>s):</para>
/// <list type="bullet">
///   <item><see cref="Cie"/>: 4 per entry — the white-balanced CIE x̄ȳz̄ (<see cref="WavelengthLookup.TryGet"/>)
///   and the wavelength in nm, the engine's <c>DeterXYZ</c> layout.</item>
///   <item><see cref="Reflectance"/>: <c>[material · Count + entry]</c>, the built-in-tier spectrum of each
///   material (<see cref="RgbToReflectanceBasis"/> applied to its linear-sRGB albedo).</item>
///   <item><see cref="Albedo"/>: 4 per material — its linear-sRGB albedo and 0, for the albedo output.</item>
/// </list>
/// A pixel-sample's XYZ is <c>Σₖ Cie[iₖ].xyz · L(λᵢₖ) / CompanionCount · DeterministicCorrection</c>, which
/// maps a flat spectrum of 1 to luminance 1: Y is the ȳ-weighted mean spectral radiance, in W·m⁻²·sr⁻¹·nm⁻¹, and
/// <see cref="LuminancePerY"/> times it is luminance in cd/m².
/// </summary>
public sealed class IntegratorTables
{
    /// <summary>Wavelengths traced together per sample (the hero plus three companions, as in the engine).</summary>
    public const int CompanionCount = 4;

    private static readonly Lazy<WavelengthLookup> SharedLookup = new(() => new WavelengthLookup());

    /// <summary>
    /// Luminance in cd/m² per unit of Y: 683 lm/W times the integral of the CIE 1931 ȳ over 360–830 nm in 1 nm
    /// steps (106.857 nm, from the engine's own table), since Y is the ȳ-weighted mean spectral radiance.
    /// </summary>
    public const double LuminancePerY = 683.0 * 106.857;

    private IntegratorTables(int count, float correction, float[] cie, float[] reflectance, float[] albedo)
    {
        Count = count;
        DeterministicCorrection = correction;
        Cie = cie;
        Reflectance = reflectance;
        Albedo = albedo;
    }

    /// <summary>Hero wavelengths in the cycle (50).</summary>
    public int Count { get; }

    /// <summary>Stride between companion wavelength indices.</summary>
    public int CompanionStride => Count / CompanionCount;

    /// <summary>Normalises a flat-white spectrum to luminance 1 through the hero sum.</summary>
    public float DeterministicCorrection { get; }

    /// <summary>White-balanced CIE response and wavelength, 4 floats per entry.</summary>
    public float[] Cie { get; }

    /// <summary>Reflectance per material per entry.</summary>
    public float[] Reflectance { get; }

    /// <summary>Linear-sRGB albedo per material, 4 floats per material.</summary>
    public float[] Albedo { get; }

    /// <summary>The wavelength in nm of entry <paramref name="index"/>.</summary>
    public float WavelengthNm(int index) => Cie[index * 4 + 3];

    /// <summary>The engine's wavelength table, built once per process (it parses the embedded CIE data).</summary>
    public static WavelengthLookup Wavelengths => SharedLookup.Value;

    /// <summary>
    /// Builds the tables for <paramref name="materials"/> (indexed by <see cref="VistaMaterial.Id"/>). Reflectance
    /// is clamped to [0, 1]: the basis is a minimum-norm fit that can dip below zero for saturated colours, which
    /// a multi-bounce path must never see.
    /// </summary>
    public static IntegratorTables Build(IReadOnlyList<VistaMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        for (int m = 0; m < materials.Count; m++)
        {
            if (materials[m].Id != m)
                throw new ArgumentException($"Material ids must equal their index; material {m} has id {materials[m].Id}.", nameof(materials));
        }

        WavelengthLookup lookup = Wavelengths;
        SpectralResources spectral = SpectralResourceBaker.Bake(lookup, []);
        int n = spectral.DeterministicCount;
        RgbToReflectanceBasis basis = RgbToReflectanceBasis.Build(spectral);

        var cie = (float[])spectral.DeterXYZ.Clone();
        var reflectance = new float[materials.Count * n];
        var albedo = new float[materials.Count * 4];
        for (int m = 0; m < materials.Count; m++)
        {
            (float r, float g, float b) = materials[m].LinearRgbAlbedo;
            float[] curve = basis.Reflectance(new Vector3(r, g, b));
            for (int i = 0; i < n; i++)
                reflectance[m * n + i] = Math.Clamp(curve[i], 0f, 1f);
            albedo[m * 4] = r;
            albedo[m * 4 + 1] = g;
            albedo[m * 4 + 2] = b;
        }

        return new IntegratorTables(n, spectral.DeterministicCorrection, cie, reflectance, albedo);
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
