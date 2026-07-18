using System.Numerics;

namespace RayTracer;

/// <summary>
/// Procedural spectral sky for the open-roof half of the maze (outdoor-area-plan.md §O3; design.md
/// Phase 9). Returns the radiance a ray sees when it escapes all geometry through the open roof: a
/// bright sun disk toward <c>−SunDirection</c>, otherwise a single-scattering <b>Rayleigh</b> dome.
///
/// <para>The dome is a genuine (if analytic) scattering integral rather than a hand-tuned tint: the
/// in-scattered radiance at wavelength λ along a view ray is
/// <c>SkyStrength · solar(λ) · phase(γ) · (1 − e^(−τ(λ)))</c>, where τ is the Rayleigh optical depth
/// <c>RayleighDepth · (550/λ)⁴ · airmass(elevation)</c>. Two things make the result read <b>blue, not
/// violet</b> — the trap the old ad-hoc <c>(550/λ)ⁿ</c> tilt fell into:</para>
/// <list type="bullet">
///   <item>the <c>1 − e^(−τ)</c> <b>saturation</b> compresses the runaway short-λ (violet) spike that a
///   raw <c>1/λ⁴</c> law produces, and whitens the dome toward the horizon (long airmass → every λ
///   saturates) — that horizon paling <i>is</i> aerial perspective; and</item>
///   <item>the <b>Rayleigh phase</b> <c>0.75·(1+cos²γ)</c> brightens the sky toward (and opposite) the
///   sun and dims the ~90° band, so the gradient tracks the sun.</item>
/// </list>
/// <para>Radiance is per-wavelength to fit the hero-wavelength pipeline: at a miss the caller
/// accumulates <c>XYZ(λ)·Radiance(dir, λ)</c>. <see cref="SunTempK"/> gives the solar spectrum a black-
/// body shape (nearly flat across the visible at daylight temperatures) and is the knob §O8 will lower
/// to redden the light at dusk.</para>
/// </summary>
public sealed class Sky
{
    /// <summary>Reference wavelength (nm) at which the Rayleigh coefficient and solar spectrum are unity.</summary>
    private const float ReferenceNm = 550f;

    /// <summary>Airmass floor added to the elevation cosine so the horizon airmass stays finite
    /// (<c>1/(cosZenith + AirmassFloor)</c>: ≈0.87 at the zenith, ≈6.7 at the horizon).</summary>
    private const float AirmassFloor = 0.15f;

    /// <summary>Second radiation constant <c>hc/k</c> in nm·K, for the solar black-body shape.</summary>
    private const float C2NmK = 1.438777e7f;

    /// <summary>Travel direction of the sunlight (the sun is toward <c>-SunDirection</c>). Normalised on set.</summary>
    public Vector3 SunDirection { get; }

    /// <summary>Angular radius of the sun disk (radians).</summary>
    public float SunAngularRadius { get; }

    /// <summary>Spectral radiance of the sun disk itself (flat across wavelength — a white sun for now).</summary>
    public float SunRadiance { get; }

    /// <summary>Overall brightness scale of the scattered dome (absorbs units + exposure).</summary>
    public float SkyStrength { get; }

    /// <summary>Rayleigh optical depth toward the zenith at the reference wavelength — the single knob that
    /// sets how blue the sky is. More depth saturates the dome toward white (which, through the discrete
    /// hero-wavelength integration, reads faintly magenta), so ~0.35 keeps a clean blue from zenith all the
    /// way down to the horizon rather than paling out low in the sky.</summary>
    public float RayleighDepth { get; }

    /// <summary>Colour temperature (K) of the sun for the solar black-body shape (5778 K ≈ white daylight).</summary>
    public float SunTempK { get; }

    private readonly Vector3 _toSun;
    private readonly float _cosSunRadius;
    private readonly float _solarDenomRef; // exp(C2/(ReferenceNm·T)) − 1, precomputed for normalisation
    private readonly float _solarLumaNorm; // §6 luminance normalisation (1 when no spectral table given)

    public Sky(
        Vector3 sunDirection,
        float sunAngularRadius = 0.04f,
        float sunRadiance = 9f,
        float skyStrength = 0.7f,
        float rayleighDepth = 0.35f,
        float sunTempK = 5778f,
        WavelengthLookup? spectral = null)
    {
        SunDirection = sunDirection.LengthSquared() > 1e-12f ? Vector3.Normalize(sunDirection) : new Vector3(0f, -1f, 0f);
        SunAngularRadius = MathF.Max(0f, sunAngularRadius);
        SunRadiance = sunRadiance;
        SkyStrength = skyStrength;
        RayleighDepth = MathF.Max(0f, rayleighDepth);
        SunTempK = MathF.Max(1f, sunTempK);
        _toSun = -SunDirection;
        _cosSunRadius = MathF.Cos(SunAngularRadius);
        _solarDenomRef = MathF.Exp(C2NmK / (ReferenceNm * SunTempK)) - 1f;
        // §6: luma-normalise the solar shape over the hero set so the sun's colour temperature shifts the
        // daylight hue without changing the dome brightness. Without a spectral table (the historical
        // constructor) the norm is 1, so the shape stays 550-normalised — existing behaviour unchanged.
        _solarLumaNorm = spectral?.BlackbodyLumaNorm(SunTempK) ?? 1f;
    }

    /// <summary>Planck black-body spectral shape at <paramref name="wavelengthNm"/>, luma-normalised over
    /// the hero set (§6) when a spectral table was supplied, else normalised to 1 at the reference
    /// wavelength (a plain shape factor, not an absolute radiance).</summary>
    private float SolarShape(float wavelengthNm)
    {
        float r = ReferenceNm / wavelengthNm;
        float denom = MathF.Exp(C2NmK / (wavelengthNm * SunTempK)) - 1f;
        return r * r * r * r * r * (_solarDenomRef / denom) * _solarLumaNorm; // (550/λ)^5 · (denomRef/denom) · norm
    }

    /// <summary>
    /// Spectral radiance toward <paramref name="dir"/> (the ray's travel direction) at
    /// <paramref name="wavelengthNm"/>: the sun disk when <paramref name="dir"/> points within the disk,
    /// otherwise the single-scattering Rayleigh dome. Below the horizon the elevation cosine is clamped
    /// to zero (holds the horizon value — only a floor for the rare ray that escapes that low).
    /// </summary>
    public float Radiance(Vector3 dir, float wavelengthNm)
    {
        Vector3 d = Vector3.Normalize(dir);

        // Sun disk — flat bright value, matching the directional light's direction + angular size.
        // Only the camera/primary miss and specular reflections see it; a diffuse bounce uses
        // DomeRadiance so the sun is not double-counted against NEE (finding §5).
        float cosSun = Vector3.Dot(d, _toSun);
        if (cosSun >= _cosSunRadius)
            return SunRadiance;

        return DomeRadiance(d, wavelengthNm);
    }

    /// <summary>
    /// Sky radiance for an indirect/bounce lookup: the single-scattering Rayleigh dome <b>without</b> the
    /// sun disk (realism finding §5). A diffuse surface's sun illumination is already added by next-event
    /// estimation on the directional sun light, so a BSDF-sampled bounce that happens to point at the sun
    /// disk must not add it a second time. Identical to <see cref="Radiance"/> away from the disk.
    /// </summary>
    public float DomeRadiance(Vector3 dir, float wavelengthNm)
    {
        Vector3 d = Vector3.Normalize(dir);
        float cosSun = Vector3.Dot(d, _toSun);

        // Optical depth grows with airmass (toward the horizon) and as 1/λ⁴ (toward the blue).
        float cosZenith = Math.Clamp(d.Y, 0f, 1f);
        float airmass = 1f / (cosZenith + AirmassFloor);
        float lambdaRatio = ReferenceNm / MathF.Max(wavelengthNm, 1f);
        float betaRel = lambdaRatio * lambdaRatio * lambdaRatio * lambdaRatio; // (550/λ)⁴
        float tau = RayleighDepth * betaRel * airmass;
        float inscatter = 1f - MathF.Exp(-tau);

        // Rayleigh phase (mean 1 over the sphere): brightens toward/away from the sun, dims at ~90°.
        float phase = 0.75f * (1f + cosSun * cosSun);

        return SkyStrength * SolarShape(wavelengthNm) * phase * inscatter;
    }
}
