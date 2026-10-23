namespace Vista.Rendering;

/// <summary>
/// The forest canopy's scattering (vista-plan §10 M1.12b): the kernel-driven BRDF MODIS fits to every land pixel (Wanner, Li and
/// Strahler 1995; Lucht, Schaaf and Strahler 2000), <c>R = ρ_iso·(1 + kv·K_vol + kg·K_geo)</c>, with RossThick's volume
/// scattering of randomly placed facets (the needles) and LiSparse-Reciprocal's crowns — spheroids twice as high as wide (h/b = 2,
/// b/r = 1, MODIS's) casting shadows on the ground between them, hidden at the hot spot where the sun stands behind the viewer. The
/// C# twin of the shader's <c>CanopyKernels</c>, operation for operation, on the three cosines a surface point has: the light's and
/// the view's against the normal, and the angle between them (the phase angle's cosine, both vectors away from the surface).
/// </summary>
public static class CanopyBrdf
{
    /// <summary>
    /// The smallest cosine of a zenith the kernels see, cos 70°: MODIS fits them within it, and beyond it LiSparse's secants run away
    /// (the hot spot at grazing light and view grows without bound).
    /// </summary>
    public const float MinCosine = 0.342f;

    /// <summary>
    /// <c>kv·K_vol + kg·K_geo</c> for light at <paramref name="cosLight"/> from the normal seen at <paramref name="cosView"/> with
    /// <paramref name="cosPhase"/> between the two directions, both zeniths held within 70° (<see cref="MinCosine"/>), and never below
    /// −1 (the shader's <c>CanopyKernels</c>): the canopy's factor on ρ_iso is one more than this.
    /// </summary>
    public static float Kernels(float cosLight, float cosView, float cosPhase, float volume, float geometric)
    {
        float ci = MathF.Max(cosLight, MinCosine), cv = MathF.Max(cosView, MinCosine);
        float cosXi = Math.Clamp(cosPhase, -1f, 1f);
        (float vol, float geo) = Unclamped(ci, cv, cosXi);
        return MathF.Max(volume * vol + geometric * geo, -1f);
    }

    /// <summary>
    /// The two kernels themselves, RossThick's <c>K_vol</c> and LiSparse-Reciprocal's <c>K_geo</c>, at any zeniths (cosines above 0):
    /// what the tests integrate against the white-sky albedos Lucht, Schaaf and Strahler give (<see cref="World.MaterialRules.VolumeKernelAlbedo"/>,
    /// <see cref="World.MaterialRules.GeometricKernelAlbedo"/>).
    /// </summary>
    public static (float Volume, float Geometric) Unclamped(float ci, float cv, float cosXi)
    {
        float xi = MathF.Acos(cosXi);
        float vol = ((0.5f * MathF.PI - xi) * cosXi + MathF.Sin(xi)) / (ci + cv) - 0.25f * MathF.PI;
        float secI = 1f / ci, secV = 1f / cv;
        float ti2 = secI * secI - 1f, tv2 = secV * secV - 1f;
        float ttCos = (cosXi - ci * cv) * (secI * secV);
        float d2 = MathF.Max(ti2 + tv2 - 2f * ttCos, 0f);
        float cross2 = MathF.Max(ti2 * tv2 - ttCos * ttCos, 0f);
        float cosT = MathF.Min(2f * MathF.Sqrt(d2 + cross2) / (secI + secV), 1f);
        float t = MathF.Acos(cosT);
        float overlap = (t - MathF.Sin(t) * cosT) * (secI + secV) * (1f / MathF.PI);
        float geo = overlap - secI - secV + 0.5f * (1f + cosXi) * secI * secV;
        return (vol, geo);
    }
}
