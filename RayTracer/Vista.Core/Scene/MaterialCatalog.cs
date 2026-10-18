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
/// <param name="LinearRgbAlbedo">For <see cref="DataTier.BuiltIn"/> materials, the linear-sRGB albedo that
/// <c>RgbToReflectanceBasis</c> upsamples to a spectrum.</param>
public sealed record VistaMaterial(int Id, string Key, DataTier Tier, (float R, float G, float B) LinearRgbAlbedo);

/// <summary>
/// The M0 material table: a single built-in terrain material (a neutral grey-brown soil/rock), so the
/// sidecar honestly reports tier 5 for everything. Rule-based rock / scree / grass / snow arrive in M1,
/// measured spectra in M2.
/// </summary>
public static class MaterialCatalog
{
    /// <summary>The M0 terrain material.</summary>
    public static VistaMaterial Terrain { get; } = new(0, "terrain", DataTier.BuiltIn, (0.18f, 0.16f, 0.14f));

    /// <summary>Every M0 material, indexed by <see cref="VistaMaterial.Id"/>.</summary>
    public static IReadOnlyList<VistaMaterial> M0 { get; } = [Terrain];

    /// <summary>Material key → tier, as recorded in the sidecar.</summary>
    public static SortedDictionary<string, DataTier> TiersOf(IEnumerable<VistaMaterial> materials) =>
        new(materials.ToDictionary(m => m.Key, m => m.Tier), StringComparer.Ordinal);
}
