using System;

namespace RayTracer;

/// <summary>
/// Which physical camera the primary rays are generated through (plan §4).
/// <see cref="Pinhole"/> is the historical behaviour — an ideal pinhole at the camera position with
/// everything in focus forever — and is what every scene gets unless it opts in.
/// </summary>
public enum LensModel
{
    /// <summary>Ideal pinhole: single origin, no aperture, no distortion, no vignette (historical).</summary>
    Pinhole = 0,
    /// <summary>Modern mobile phone: tiny sensor, fast fixed aperture, wide, near-infinite depth of field.</summary>
    Phone = 1,
    /// <summary>Simple/old camera: fixed focus, small aperture, barrel distortion, heavy vignette, soft corners.</summary>
    Simple = 2,
    /// <summary>Expensive cine lens: zoom, rack focus, aperture ring, bladed iris, near-zero distortion.</summary>
    Cine = 3,
}

/// <summary>
/// The lens and sensor a frame is shot through (plan §4). Follows the <see cref="RealismOptions"/> /
/// <see cref="VolumetricOptions"/> shape: <b>every field defaults to the pinhole</b>, so a render that
/// does not opt in is bit-for-bit unchanged — the aperture path draws no random numbers when the
/// aperture radius is zero, which keeps the wavelength/RNG stream (and therefore every golden) identical.
///
/// <para><b>Units.</b> The world is metres (a maze cell is <see cref="MazeGeometryBuilder.CellSize"/> = 2 m
/// wide, walls 2 m tall), so lens dimensions given in millimetres convert with a factor of 1/1000. This is
/// what makes the depth of field come out at real photographic magnitudes rather than an arbitrary blur.</para>
///
/// <para><b>Framing.</b> <see cref="FocalLengthMm"/> = 0 means "keep the <see cref="Camera.Fov"/> the scene
/// asked for and derive the implied focal length from it" (see <see cref="EffectiveFocalLengthMm"/>). So
/// turning a lens on changes focus/bokeh/distortion but does <i>not</i> reframe the shot. Setting a non-zero
/// focal length is the zoom, and it does reframe — that is what a zoom is.</para>
/// </summary>
/// <param name="Model">Which lens character this is. Only used to label the preset; the fields below are
/// what actually drive rendering, so a scene can start from a model and override any single knob.</param>
/// <param name="SensorHeightMm">Sensor/film height. Sets the focal length ⇄ field-of-view relationship and,
/// with it, how much defocus a given f-stop buys. Full-frame is 24; Super35 is 18.66; a phone's 1/1.7" is 5.7.</param>
/// <param name="FocalLengthMm">Focal length — the zoom. <c>0</c> (default) means "derive it from the camera's
/// <see cref="Camera.Fov"/> and <see cref="SensorHeightMm"/>", which preserves the scene's framing.</param>
/// <param name="FStop">Aperture as an f-number (f/N): aperture diameter = focal length / N. <c>0</c> (default)
/// means a pinhole — no aperture sampling at all, so the RNG stream is untouched. Smaller = wider = more bokeh.</param>
/// <param name="FocusDistance">Distance in metres from the camera to the plane that is rendered sharp.</param>
/// <param name="ApertureScale">A <b>synthetic</b> multiplier on the physical aperture radius. <c>1</c> is
/// physical. This is the phone's "portrait mode": a real phone lens has near-infinite depth of field, so the
/// only way to get its blurred-background look is to defocus with an aperture larger than the physics allows.</param>
/// <param name="ApertureBlades">Number of iris blades. <c>0</c> (default) is a perfect circle; <c>&gt;= 3</c>
/// makes out-of-focus highlights take the blade polygon's shape.</param>
/// <param name="BladeRotation">Rotation of the blade polygon, in radians.</param>
/// <param name="DistortionK1">Brown–Conrady radial distortion, r² term. Negative = barrel (straight walls bow
/// outward, the old-camera look); positive = pincushion. <c>0</c> = rectilinear.</param>
/// <param name="DistortionK2">Radial distortion, r⁴ term — shapes the falloff toward the corners.</param>
/// <param name="VignetteStrength">Corner darkening, as an exponent on the physical <c>cos⁴θ</c> natural
/// falloff (see <see cref="LensSampler.VignetteFactor"/>): <c>0</c> = off and exactly flat (historical), <c>1</c> =
/// physically correct, above 1 = exaggerated the way a cheap deep-barrel lens is.</param>
/// <param name="CatEyeVignette">When set, the aperture is clipped toward the frame edge, so off-axis bokeh
/// takes the squashed "cat's eye" shape a real lens barrel produces (mechanical vignetting).</param>
public readonly record struct LensOptions(
    LensModel Model = LensModel.Pinhole,
    float SensorHeightMm = 24f,
    float FocalLengthMm = 0f,
    float FStop = 0f,
    float FocusDistance = 3f,
    float ApertureScale = 1f,
    int ApertureBlades = 0,
    float BladeRotation = 0f,
    float DistortionK1 = 0f,
    float DistortionK2 = 0f,
    float VignetteStrength = 0f,
    bool CatEyeVignette = false)
{
    /// <summary>World units (metres) per millimetre — the maze is modelled at human scale.</summary>
    public const float MetresPerMm = 0.001f;

    /// <summary>
    /// The ideal pinhole — the historical camera, and the default.
    /// <para><b>Use this, never <c>new LensOptions()</c>.</b> For a <c>record struct</c> the parameterless
    /// constructor (and <c>default</c>) zero-inits and <i>bypasses</i> the primary constructor's defaults,
    /// which would leave <see cref="SensorHeightMm"/> and <see cref="ApertureScale"/> at 0. Passing one
    /// explicit argument forces the primary constructor, so the omitted fields take their proper defaults.
    /// Same trap as <see cref="RealismOptions.Historical"/>.</para>
    /// </summary>
    public static LensOptions Pinhole => new(Model: LensModel.Pinhole);

    /// <summary>
    /// The lens characters of plan §4.2–4.4. Each keeps <see cref="FocalLengthMm"/> at 0, so selecting one
    /// does not reframe the scene; the differences are focus, bokeh, distortion and vignette.
    /// </summary>
    public static LensOptions FromModel(LensModel model)
    {
        return model switch
        {
            // §4.2 — 1/1.7" sensor, fast fixed aperture, wide. Physically this has so much depth of field
            // that nothing defocuses at maze distances; the phone look is the *computational* one, so the
            // character here is the mild barrel, the vignette, and (opt-in) a synthetic portrait aperture.
            LensModel.Phone => new LensOptions(
                Model: LensModel.Phone,
                SensorHeightMm: 5.7f,
                FStop: 1.8f,
                FocusDistance: 2.5f,
                DistortionK1: -0.04f,
                VignetteStrength: 0.8f),

            // §4.3 — a box-camera toy lens: stopped well down (so little defocus), but bowing straight walls,
            // darkening the corners hard, and softening them. The most visually distinct from the pinhole.
            LensModel.Simple => new LensOptions(
                Model: LensModel.Simple,
                SensorHeightMm: 24f,
                FStop: 8f,
                FocusDistance: 4f,
                DistortionK1: -0.12f,
                DistortionK2: -0.03f,
                VignetteStrength: 2.2f),

            // §4.4 — Super35, wide open, with a 7-blade iris and cat's-eye edge vignetting. Note the honest
            // physics: at the scene's ~60° field of view even f/1.4 is only a modest defocus. Reaching for the
            // creamy-bokeh look means zooming (setting FocalLengthMm), which reframes — that is what a zoom is.
            LensModel.Cine => new LensOptions(
                Model: LensModel.Cine,
                SensorHeightMm: 18.66f,
                FStop: 1.4f,
                FocusDistance: 3f,
                ApertureBlades: 7,
                VignetteStrength: 1f,
                CatEyeVignette: true),

            _ => Pinhole,
        };
    }

    /// <summary>
    /// "Portrait mode": the phone with a synthetic aperture <paramref name="scale"/>× wider than its optics
    /// physically permit, which is the only way to get a phone's blurred background (plan §4.2).
    /// </summary>
    public static LensOptions PhonePortrait(float scale = 12f) =>
        FromModel(LensModel.Phone) with { ApertureScale = scale, FocusDistance = 1.5f };

    /// <summary>
    /// The focal length actually used, in mm: <see cref="FocalLengthMm"/> when set, otherwise the one implied
    /// by the camera's vertical field of view and the sensor height (<c>f = h / (2·tan(fov/2))</c>) — which is
    /// what keeps the scene's framing when a lens is switched on.
    /// </summary>
    public float EffectiveFocalLengthMm(float cameraFov)
    {
        if (FocalLengthMm > 0f)
            return FocalLengthMm;
        float t = MathF.Tan(cameraFov * 0.5f);
        return t > 1e-6f ? SensorHeightMm / (2f * t) : SensorHeightMm;
    }

    /// <summary>
    /// The vertical field of view this lens presents, in radians. Equal to <paramref name="cameraFov"/> unless
    /// a <see cref="FocalLengthMm"/> (a zoom) has been set, in which case the lens reframes the shot.
    /// </summary>
    public float EffectiveFov(float cameraFov)
    {
        if (Model == LensModel.Pinhole || FocalLengthMm <= 0f)
            return cameraFov;
        return 2f * MathF.Atan(SensorHeightMm / (2f * FocalLengthMm));
    }

    /// <summary>
    /// Aperture radius in world units (metres): <c>(f / 2N) · ApertureScale</c>, converted from mm. Zero for a
    /// pinhole or an unset f-stop — and zero is the signal to the ray generator to skip aperture sampling
    /// entirely, leaving the RNG stream (and the goldens) untouched.
    /// </summary>
    public float ApertureRadiusWorld(float cameraFov)
    {
        if (Model == LensModel.Pinhole || FStop <= 0f || ApertureScale <= 0f)
            return 0f;
        float f = EffectiveFocalLengthMm(cameraFov);
        return f / (2f * FStop) * ApertureScale * MetresPerMm;
    }

    /// <summary>True when this lens bends the primary ray radially (barrel/pincushion).</summary>
    public bool HasDistortion => Model != LensModel.Pinhole && (DistortionK1 != 0f || DistortionK2 != 0f);

    /// <summary>True when the frame corners darken beyond the pinhole's uniform response.</summary>
    public bool HasVignette => Model != LensModel.Pinhole && VignetteStrength > 0f;
}
