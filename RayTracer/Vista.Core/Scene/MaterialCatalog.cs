using Vista.Rendering;

namespace Vista.Scene;

/// <summary>
/// Where a material's spectrum came from (vista-plan §6.2). Lower is better; every render records the tier
/// each material used, and "require measured data" refuses anything above <see cref="ManualImport"/>.
/// </summary>
public enum DataTier
{
    /// <summary>Measured data already in the local cache.</summary>
    LocalCache = 1,
    /// <summary>Measured data downloaded (checksum-verified) for this render.</summary>
    Download = 2,
    /// <summary>Measured data the user imported by hand (order-form sources).</summary>
    ManualImport = 3,
    /// <summary>A physical model fitted to measured data (PROSPECT-D for leaves).</summary>
    Model = 4,
    /// <summary>Built-in fallback: an analytic fit or RGB upsampling. Always available, no network.</summary>
    BuiltIn = 5,
}

/// <summary>One material the integrator can shade.</summary>
/// <param name="Id">Index the renderer writes into the material-id output.</param>
/// <param name="Key">Stable name recorded in the sidecar.</param>
/// <param name="Tier">Where its spectrum comes from.</param>
/// <param name="LinearRgbAlbedo">Its linear-sRGB albedo: for a material without a <see cref="Spectrum"/>, the colour
/// <c>ReflectanceBasis</c> upsamples to a spectrum; for one with, the spectrum's own colour (the albedo output).</param>
public sealed record VistaMaterial(int Id, string Key, DataTier Tier, (float R, float G, float B) LinearRgbAlbedo)
{
    /// <summary>
    /// The reflectance spectrum as (wavelength in nm, reflectance) pairs, rising in wavelength, linear between them and flat past
    /// the ends (vista-plan §10 M1.12); null to upsample <see cref="LinearRgbAlbedo"/> instead.
    /// </summary>
    public IReadOnlyList<double>? Spectrum { get; init; }

    /// <summary>
    /// How rough the surface is for Oren–Nayar-style diffuse reflection (Fujii's form: 0 is Lambert, 1 very rough), so a low sun
    /// behind the camera lights it back toward the viewer (vista-plan §10 M1.12).
    /// </summary>
    public float Roughness { get; init; }
}

/// <summary>
/// The materials. M0 had one built-in terrain material (a neutral grey-brown soil/rock); since M1.12 the ground has five, which
/// the rules blend per hit (<see cref="World.MaterialRules"/>): two rocks (a pale hard one and a dark soft one, which a bed's
/// hardness mixes), scree, soil and alpine grass. Their spectra are built in (tier 5): authored from the typical shapes of each
/// class — rock faces grey and darker than the fresh debris below them (weathering and lichen darken a face), both nearly flat
/// (gneiss and schist debris is grey; the beds' own tints warm or cool them), soil rising toward the red (its iron oxides), grass with
/// chlorophyll's absorption in the blue and the red, its green peak and its red edge (which no hero wavelength reaches: they lie
/// between 419 and 651 nm) — not fitted to any one sample; measured spectra arrive in M2. The sidecar records each one's tier.
/// Since M1.12b a sixth, forest: a conifer canopy as a whole seen from above, needles, shadowed crowns and the gaps between them
/// together, so its reflectance is far below a leaf's (a few percent in the visible, the green peak small, the red edge past the
/// hero wavelengths), the albedo under a white sky that the canopy's kernels are normalised to (<see cref="World.MaterialRules.CanopyAlbedoScale"/>).
/// Since M1.13 four more, snow and ice, each from ice's absorption and its grain size (<see cref="SnowSpectrum"/>, tier 5): the season's
/// dry snow (grains 0.2 mm, nearly clean), its wet snow (1.5 mm grains, its dust gathered at the surface), a glacier's firn (2.5 mm,
/// years of dust) and its bare ice (bubbles some 25 mm apart, grey with the dust and fine debris of its melt), all Lambertian or nearly so.
/// </summary>
public static class MaterialCatalog
{
    /// <summary>Id of the hard rock in <see cref="Ground"/> (the shader's too).</summary>
    public const int RockHardId = 0;

    /// <summary>Id of the soft rock.</summary>
    public const int RockSoftId = 1;

    /// <summary>Id of the scree.</summary>
    public const int ScreeId = 2;

    /// <summary>Id of the soil.</summary>
    public const int SoilId = 3;

    /// <summary>Id of the grass.</summary>
    public const int GrassId = 4;

    /// <summary>Id of the forest (vista-plan §10 M1.12b).</summary>
    public const int ForestId = 5;

    /// <summary>Id of the season's dry snow (vista-plan §10 M1.13).</summary>
    public const int SnowId = 6;

    /// <summary>Id of a glacier's bare ice.</summary>
    public const int IceId = 7;

    /// <summary>Id of a glacier's firn.</summary>
    public const int FirnId = 8;

    /// <summary>Id of the season's wet snow (blended with the dry by how wet it is; the material-id output shows both as snow).</summary>
    public const int WetSnowId = 9;

    /// <summary>The M0 terrain material.</summary>
    public static VistaMaterial Terrain { get; } = new(0, "terrain", DataTier.BuiltIn, (0.18f, 0.16f, 0.14f));

    /// <summary>Every M0 material, indexed by <see cref="VistaMaterial.Id"/>: what <c>--materials off</c> renders.</summary>
    public static IReadOnlyList<VistaMaterial> M0 { get; } = [Terrain];

    /// <summary>The ground's materials (vista-plan §10 M1.12), indexed by <see cref="VistaMaterial.Id"/>.</summary>
    public static IReadOnlyList<VistaMaterial> Ground { get; } =
    [
        Spectral(RockHardId, "rock.hard", 0.6f,
            [380, 0.165, 400, 0.17, 450, 0.18, 500, 0.186, 550, 0.19, 600, 0.194, 650, 0.197, 700, 0.2, 750, 0.203]),
        Spectral(RockSoftId, "rock.soft", 0.6f,
            [380, 0.07, 400, 0.072, 450, 0.076, 500, 0.079, 550, 0.082, 600, 0.085, 650, 0.088, 700, 0.091, 750, 0.094]),
        Spectral(ScreeId, "scree", 0.75f,
            [380, 0.195, 400, 0.2, 450, 0.212, 500, 0.222, 550, 0.23, 600, 0.237, 650, 0.243, 700, 0.248, 750, 0.252]),
        Spectral(SoilId, "soil", 0.5f,
            [380, 0.035, 400, 0.04, 450, 0.05, 500, 0.062, 550, 0.078, 600, 0.10, 650, 0.12, 700, 0.135, 750, 0.15]),
        Spectral(GrassId, "grass", 0.4f,
            [380, 0.032, 420, 0.035, 450, 0.04, 480, 0.046, 500, 0.058, 520, 0.092, 540, 0.122, 550, 0.128, 560, 0.123, 580, 0.1, 600, 0.07,
             620, 0.052, 640, 0.042, 660, 0.033, 680, 0.032, 700, 0.08, 720, 0.25, 750, 0.40]),
        Spectral(ForestId, "forest", 1.0f,
            [380, 0.018, 400, 0.019, 450, 0.021, 480, 0.023, 500, 0.026, 520, 0.034, 540, 0.043, 550, 0.045, 560, 0.044, 580, 0.038, 600, 0.031,
             620, 0.026, 640, 0.023, 660, 0.021, 680, 0.02, 700, 0.045, 720, 0.12, 750, 0.20]),
        Spectral(SnowId, "snow", 0f, SnowSpectrum.Spectrum(0.2e-3, 0.02, 3.0)),
        Spectral(IceId, "ice", 0.1f, SnowSpectrum.Spectrum(25e-3, 1.2, 1.5)),
        Spectral(FirnId, "firn", 0.05f, SnowSpectrum.Spectrum(2.5e-3, 0.8, 3.0)),
        Spectral(WetSnowId, "snow.wet", 0f, SnowSpectrum.Spectrum(1.5e-3, 0.4, 3.0)),
    ];

    /// <summary>The materials a render shades: the ground's by rule, or M0's one.</summary>
    public static IReadOnlyList<VistaMaterial> For(bool materials) => materials ? Ground : M0;

    /// <summary>Material key → tier, as recorded in the sidecar.</summary>
    public static SortedDictionary<string, DataTier> TiersOf(IEnumerable<VistaMaterial> materials) =>
        new(materials.ToDictionary(m => m.Key, m => m.Tier), StringComparer.Ordinal);

    /// <summary>
    /// A built-in spectral material: its albedo is its spectrum's colour through the default quadrature (a render's own tables take
    /// it through the render's, <see cref="IntegratorTables.Build"/>).
    /// </summary>
    private static VistaMaterial Spectral(int id, string key, float roughness, double[] spectrum)
    {
        (float r, float g, float b) = WavelengthTable.For(WavelengthTable.DefaultQuadrature).LinearRgbOf(spectrum);
        return new VistaMaterial(id, key, DataTier.BuiltIn, (r, g, b)) { Spectrum = spectrum, Roughness = roughness };
    }
}
