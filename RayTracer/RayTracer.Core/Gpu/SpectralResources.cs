namespace RayTracer;

/// <summary>
/// GPU-ready spectral tables baked from <see cref="WavelengthLookup"/> and the
/// scene materials.
///
/// The CPU renderer only ever evaluates spectra at the 50 deterministic hero
/// wavelengths (both the hero and its companions are entries of the
/// deterministic cycle), so everything the fullbright path needs collapses to
/// two small tables indexed by deterministic index <c>0..49</c>:
/// <list type="bullet">
///   <item><see cref="DeterXYZ"/> — the CIE XYZ response at each hero wavelength.</item>
///   <item><see cref="MaterialReflectance"/> — <c>max(SCI, SCE)</c> reflectance
///   per material at each hero wavelength.</item>
/// </list>
/// This matches <c>WavelengthLookup.TryGet</c> and
/// <c>MaterialData.GetSpectralReflectance</c> exactly at those wavelengths.
/// </summary>
public sealed class SpectralResources
{
    /// <summary>Number of deterministic hero wavelengths (the cycle length, 50).</summary>
    public int DeterministicCount { get; }

    /// <summary>Number of materials in <see cref="MaterialReflectance"/> rows.</summary>
    public int MaterialCount { get; }

    /// <summary>The deterministic hero wavelengths in nanometres, one per index.</summary>
    public int[] DeterWavelengths { get; }

    /// <summary>
    /// CIE XYZ at each hero wavelength, four floats per entry (XYZ + padding) so
    /// it maps onto an HLSL <c>StructuredBuffer&lt;float4&gt;</c>.
    /// Length is <c>DeterministicCount * 4</c>.
    /// </summary>
    public float[] DeterXYZ { get; }

    /// <summary>
    /// Reflectance per material per hero wavelength, row-major:
    /// <c>MaterialReflectance[material * DeterministicCount + index]</c>.
    /// </summary>
    public float[] MaterialReflectance { get; }

    /// <summary>
    /// Normalisation factor so a perfect diffuse white integrates to luminance
    /// 1 (equals <c>WavelengthLookup.DeterministicCorrection</c>).
    /// </summary>
    public float DeterministicCorrection { get; }

    internal SpectralResources(
        int deterministicCount,
        int materialCount,
        int[] deterWavelengths,
        float[] deterXyz,
        float[] materialReflectance,
        float deterministicCorrection)
    {
        DeterministicCount = deterministicCount;
        MaterialCount = materialCount;
        DeterWavelengths = deterWavelengths;
        DeterXYZ = deterXyz;
        MaterialReflectance = materialReflectance;
        DeterministicCorrection = deterministicCorrection;
    }
}

/// <summary>
/// Bakes <see cref="SpectralResources"/> from a <see cref="WavelengthLookup"/>
/// and the ordered material list produced by <see cref="GpuScenePacker"/>.
/// </summary>
public static class SpectralResourceBaker
{
    /// <summary>Builds the GPU spectral tables.</summary>
    /// <param name="wavelengths">The shared spectral/CIE lookup.</param>
    /// <param name="materials">Materials in packer-row order.</param>
    public static SpectralResources Bake(WavelengthLookup wavelengths, IReadOnlyList<MaterialData> materials)
    {
        ArgumentNullException.ThrowIfNull(wavelengths);
        ArgumentNullException.ThrowIfNull(materials);

        int n = wavelengths.DeterministicCount;

        var deterWavelengths = new int[n];
        var deterXyz = new float[n * 4];
        for (int i = 0; i < n; i++)
        {
            int wl = wavelengths.GetDeterministicWavelength(i);
            deterWavelengths[i] = wl;
            wavelengths.TryGet(wl, out System.Numerics.Vector3 xyz);
            deterXyz[i * 4 + 0] = xyz.X;
            deterXyz[i * 4 + 1] = xyz.Y;
            deterXyz[i * 4 + 2] = xyz.Z;
            deterXyz[i * 4 + 3] = 0f;
        }

        var reflectance = new float[materials.Count * n];
        for (int m = 0; m < materials.Count; m++)
        {
            MaterialData material = materials[m];
            int rowBase = m * n;
            for (int i = 0; i < n; i++)
                reflectance[rowBase + i] = material.GetSpectralReflectance(deterWavelengths[i]);
        }

        return new SpectralResources(
            n,
            materials.Count,
            deterWavelengths,
            deterXyz,
            reflectance,
            wavelengths.DeterministicCorrection);
    }
}
