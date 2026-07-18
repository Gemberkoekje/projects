namespace RayTracer;

/// <summary>
/// Display-space tone-mapping operator applied to the resolved linear colour before the sRGB
/// transfer (realism finding §14). <see cref="None"/> is the historical behaviour — a straight
/// XYZ→sRGB with a per-channel clip, which hue-shifts highlights toward the primaries.
/// </summary>
public enum ToneMapping
{
    /// <summary>No tone map — straight XYZ→linear-sRGB then clip (the pre-realism behaviour).</summary>
    None = 0,
    /// <summary>Exposure scale only (<c>c·Exposure</c>), then clip.</summary>
    Exposure = 1,
    /// <summary>Reinhard <c>c/(1+c)</c> after the exposure scale — a soft global roll-off.</summary>
    Reinhard = 2,
    /// <summary>A cheap ACES-style filmic curve after the exposure scale.</summary>
    Filmic = 3,
}

/// <summary>Quality tier used to map a preset to a concrete set of realism knobs.
/// Mirrors <see cref="VolumetricQuality"/> / <see cref="CausticQuality"/>.</summary>
public enum RealismQuality
{
    Low = 0,
    Medium = 1,
    High = 2,
    Ultra = 3,
}

/// <summary>
/// Toggles and scalars that select, per feature, between a lifelike physical path and a cheaper or
/// more stylised shortcut (the "shortcuts" half of the adversarial rendering review). Every field
/// <b>defaults to the historical behaviour</b>, so <c>new RealismOptions()</c> — and any render that
/// does not opt in — is bit-for-bit unchanged. Presets dial in the per-tier values via
/// <see cref="FromQuality"/>; individual scenes can override any single knob with a <c>with</c>.
/// </summary>
/// <param name="SpecularIndirect">Finding §7. When <c>true</c>, the diffuse surface a mirror/glass
/// chain terminates on receives a one-bounce indirect term instead of only <c>ρ·(ambient+direct)</c>,
/// so reflected worlds are not conspicuously darker than the real one. Costs extra rays per specular
/// terminal. Default <c>false</c> (the historical ambient-only terminal).</param>
/// <param name="ViewDependentAlbedo">Finding §11. When <c>true</c> (default), brick/tile surfaces keep
/// the <c>0.4 + 0.6·cosθ</c> view-dependent attenuation — a reciprocity-violating shading hack that
/// reads as baked-in ambient occlusion and is the engine's established look. When <c>false</c> they use
/// a constant (view-independent) albedo.</param>
/// <param name="AreaLightSolidAngleSampling">Finding §10. When <c>true</c>, a spherical area light's
/// shadow ray targets only the <b>visible cap</b> the sphere subtends (PBRT uniform-cone) instead of a
/// uniform point over the whole sphere, so the self-occluded back hemisphere no longer contributes and
/// the near-contact penumbra tightens. The caller keeps the existing <c>cos/d²</c> geometry weight (it is
/// <i>not</i> re-derived into a solid-angle pdf × emitter cosine), so this reshapes/tightens the penumbra
/// and lifts near-field brightness rather than being a variance-only reweighting; far-field is unchanged.
/// Default <c>false</c> reproduces the historical whole-sphere uniform sampling.</param>
/// <param name="BubbleReflectBoost">Finding §12. Multiplier on a soap bubble's Fresnel reflectance
/// (<see cref="Optics.BubbleReflectBoost"/>). Default <c>45</c> is the stylised look where the thin-film
/// rings read across the whole shell; ~1 is physically faithful (a bubble reflects ≲4% off-rim).</param>
/// <param name="NestedDielectricStack">Finding §13. When <c>true</c>, refraction maintains a small
/// medium stack (IOR + absorption σ) so nested/adjacent dielectrics and bubbles-inside-glass keep the
/// correct medium. Default <c>false</c> reproduces the historical single <c>inGlass</c> boolean.</param>
/// <param name="ToneMapping">Finding §14. Display-space operator applied before the sRGB transfer.
/// Default <see cref="ToneMapping.None"/> is the historical straight clip.</param>
/// <param name="Exposure">Finding §14. Linear exposure multiplier applied ahead of the
/// <see cref="ToneMapping"/> operator. <c>1</c> = neutral. Ignored when <see cref="ToneMapping"/> is
/// <see cref="ToneMapping.None"/>, so the default is a no-op.</param>
public readonly record struct RealismOptions(
    bool SpecularIndirect = false,
    bool ViewDependentAlbedo = true,
    bool AreaLightSolidAngleSampling = false,
    float BubbleReflectBoost = Optics.BubbleReflectBoost,
    bool NestedDielectricStack = false,
    ToneMapping ToneMapping = ToneMapping.None,
    float Exposure = 1f)
{
    /// <summary>
    /// The historical-behaviour defaults (ViewDependentAlbedo on, BubbleReflectBoost 45, tone map off, …).
    /// <para><b>Use this, never <c>new RealismOptions()</c>.</b> For a <c>record struct</c> the
    /// parameterless <c>new RealismOptions()</c> (and <c>default</c>) invoke the implicit zero-init
    /// constructor, which <i>bypasses</i> the primary constructor's default parameter values — leaving
    /// ViewDependentAlbedo false and BubbleReflectBoost 0. Passing any explicit argument (here a dummy
    /// <c>SpecularIndirect: false</c>) forces the primary constructor, so the omitted parameters take
    /// their proper defaults.</para>
    /// </summary>
    public static RealismOptions Historical => new(SpecularIndirect: false);

    /// <summary>
    /// Maps a quality tier to its realism knobs. Low/Medium reproduce the historical behaviour exactly;
    /// High/Ultra opt into the physical paths whose cost the higher tiers can absorb. Aesthetic knobs
    /// (<see cref="ViewDependentAlbedo"/>, <see cref="BubbleReflectBoost"/>) keep their established look
    /// on every tier and are changed only by an explicit scene override.
    /// </summary>
    public static RealismOptions FromQuality(RealismQuality quality)
    {
        return quality switch
        {
            // High/Ultra can absorb the cost of the physical paths and get the nicer display curve; Low/
            // Medium keep the historical straight clip and cheaper shortcuts. Passing an explicit named
            // argument invokes the primary constructor, so the omitted knobs take their proper defaults.
            RealismQuality.High => new RealismOptions(ToneMapping: ToneMapping.Filmic, SpecularIndirect: true, AreaLightSolidAngleSampling: true),
            RealismQuality.Ultra => new RealismOptions(ToneMapping: ToneMapping.Filmic, SpecularIndirect: true, AreaLightSolidAngleSampling: true),
            _ => Historical,
        };
    }
}
