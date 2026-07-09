namespace RayTracer;

/// <summary>
/// Selects which per-pixel debug visualization the renderer resolves to the
/// display buffer. <see cref="Beauty"/> is the normal shaded image; every other
/// mode maps an internal buffer (variance, history, clamp, depth, normals,
/// lighting/bounce splits, reprojection diffs, …) to a false-color view.
///
/// Reconstructed from the full set of members referenced across
/// <c>DebugBufferRenderer</c>, <c>JobSystem</c>, the WinForms app, the tests,
/// and the benchmark. Values are by-name only (nothing casts to/from the
/// underlying integer), so the ordering here is cosmetic.
/// </summary>
public enum DebugViewMode
{
    /// <summary>Normal shaded, scene-referred beauty image.</summary>
    Beauty = 0,

    /// <summary>Effective accumulated sample count per pixel.</summary>
    SampleCount,

    /// <summary>Total luminance variance heatmap.</summary>
    Variance,

    /// <summary>Direct-lighting luminance variance.</summary>
    DirectVariance,

    /// <summary>Indirect-lighting luminance variance.</summary>
    IndirectVariance,

    /// <summary>Combined direct/indirect variance split view.</summary>
    VarianceSplit,

    /// <summary>TAA history blend weight (0 = current only, 1 = history dominated).</summary>
    HistoryWeight,

    /// <summary>Age of the reused temporal history per pixel.</summary>
    HistoryAge,

    /// <summary>History rejection mask (reused vs. rejected).</summary>
    RejectionMask,

    /// <summary>Sample-clamp activity heatmap.</summary>
    ClampHeatmap,

    /// <summary>Linear depth / distance from the camera.</summary>
    Depth,

    /// <summary>Surface albedo (scalar reflectance).</summary>
    Albedo,

    /// <summary>World-space surface normal as RGB.</summary>
    Normal,

    /// <summary>Direct lighting contribution only.</summary>
    DirectLighting,

    /// <summary>Indirect lighting contribution only.</summary>
    IndirectLighting,

    /// <summary>Emissive lighting contribution only.</summary>
    EmissiveLighting,

    /// <summary>First (direct) bounce contribution.</summary>
    Bounce0,

    /// <summary>Second (one-bounce indirect) contribution.</summary>
    Bounce1,

    /// <summary>Third and later bounce contribution.</summary>
    Bounce2Plus,

    /// <summary>Per-bounce contribution packed as RGB.</summary>
    BounceRGB,

    /// <summary>Difference between the current sample and the accumulated mean.</summary>
    CurrentVsAccumDiff,

    /// <summary>Difference between the unfiltered and spatially-filtered image.</summary>
    UnfilteredVsFilteredDiff,

    /// <summary>Difference between the reprojected history and the current frame.</summary>
    ReprojectedVsCurrentDiff,

    /// <summary>Edge-disagreement visualization between passes.</summary>
    EdgeDisagreement,

    /// <summary>Diffuse irradiance cache state.</summary>
    DiffuseCache,
}
