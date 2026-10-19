using System.Numerics;
using RayTracer;

namespace Vista.Rendering;

/// <summary>
/// The M0 light: the engine's single-scattering Rayleigh <see cref="Sky"/> dome plus a directional sun, both
/// driven by the placeholder <see cref="SunState"/> (vista-plan §8.1, §10 M0). This is integrator v0's whole
/// light model and the C# reference for its HLSL (vista-plan §9: a small reference and a parity test for
/// shared shader maths only). The full atmosphere and real astronomy replace it in M1.
///
/// <para><b>Dome:</b> <see cref="DomeRadiance"/> is <see cref="Sky.DomeRadiance"/> with the solar shape
/// luma-normalised over the hero set (the <see cref="Sky"/> constructor given a <see cref="WavelengthLookup"/>,
/// as the maze's shader does), so a test pins the two together.</para>
///
/// <para><b>Sun:</b> a disk of the real sun's angular radius with spectral irradiance
/// <c>PlaceholderSunIrradiance · SunLightLevel · solarShape(λ)</c> at normal incidence. The integrator reaches
/// it only through next-event estimation; bounce rays that escape see <see cref="DomeRadiance"/> without the
/// disk (the engine's realism finding §5), so the sun is never counted twice. Camera rays that hit the disk
/// see <see cref="SunDiskRadiance"/>, the radiance whose irradiance over the disk is that same value.
/// Unlike the engine's <see cref="Sky.Radiance"/> (a flat 9 for the maze's 0.04 rad sun), disk and lighting
/// agree, so the EXR stays physically consistent.</para>
/// </summary>
public sealed record SkyLight
{
    /// <summary>
    /// Spectral irradiance of the full sun at normal incidence, in the dome's units. Chosen so the "day" sun on
    /// level ground gives about five times the dome's irradiance (≈ 0.87 at 550 nm), the clear-sky ratio of
    /// direct to diffuse daylight. A placeholder until the M1 atmosphere derives it.
    /// </summary>
    public const float PlaceholderSunIrradiance = 4.5f;

    /// <summary>Angular radius of the real sun (0.2666°) in radians.</summary>
    public const float SunAngularRadiusRadians = 0.004653f;

    /// <summary>The engine's <see cref="Sky"/> default Rayleigh depth, kept for the M0 dome.</summary>
    public const float DefaultRayleighDepth = 0.35f;

    /// <summary>Reference wavelength (nm) of the Rayleigh coefficient (as in <see cref="Sky"/>).</summary>
    public const float ReferenceNm = 550f;

    /// <summary>Airmass floor of the dome (as in <see cref="Sky"/>).</summary>
    public const float AirmassFloor = 0.15f;

    /// <summary>Unit vector toward the sun.</summary>
    public required Vector3 ToSun { get; init; }

    /// <summary>Overall brightness of the dome (<see cref="SunState.SkyStrength"/>).</summary>
    public required float SkyStrength { get; init; }

    /// <summary>Rayleigh optical depth toward the zenith at 550 nm.</summary>
    public float RayleighDepth { get; init; } = DefaultRayleighDepth;

    /// <summary>Blackbody temperature of the sunlight in kelvin (shapes both the sun and the dome).</summary>
    public required float SunTempK { get; init; }

    /// <summary>Sun irradiance at normal incidence before the solar shape (0 when the sun is down).</summary>
    public required float SunIrradiance { get; init; }

    /// <summary>Angular radius of the sun disk in radians.</summary>
    public float SunAngularRadius { get; init; } = SunAngularRadiusRadians;

    /// <summary>True when the sun is above the horizon and lights the scene.</summary>
    public bool SunUp => ToSun.Y > 0f && SunIrradiance > 0f;

    /// <summary>
    /// <c>1 − cos</c> of <see cref="SunAngularRadius"/>, as <c>2·sin²(θ/2)</c> in double: for the real sun
    /// (≈ 1.1·10⁻⁵) the float subtraction <c>1 − cos θ</c> loses about 0.2%. Directions whose
    /// <c>1 − cos</c> to the sun is at most this are on the disk.
    /// </summary>
    public double SunOneMinusCosMax
    {
        get
        {
            double half = Math.Sin(SunAngularRadius * 0.5);
            return 2.0 * half * half;
        }
    }

    /// <summary>Solid angle of the sun disk in steradians.</summary>
    public float SunSolidAngle => (float)(2.0 * Math.PI * SunOneMinusCosMax);

    /// <summary>The M0 light for <paramref name="sun"/> (the <see cref="DayNightCycle"/> placeholder).</summary>
    public static SkyLight For(SunState sun)
    {
        Vector3 toSun = sun.Direction.LengthSquared() > 1e-12f ? -Vector3.Normalize(sun.Direction) : Vector3.UnitY;
        return new SkyLight
        {
            ToSun = toSun,
            SkyStrength = sun.SkyStrength,
            SunTempK = MathF.Max(1f, sun.TempK),
            SunIrradiance = toSun.Y > 0f ? PlaceholderSunIrradiance * Math.Clamp(sun.SunLightLevel, 0f, 1f) : 0f,
        };
    }

    /// <summary>
    /// The solar spectral shape at <paramref name="wavelengthNm"/>: the Planck shape at <see cref="SunTempK"/>,
    /// luma-normalised over the hero set of <paramref name="lookup"/> so the temperature changes hue, not
    /// brightness (the engine's finding §6).
    /// </summary>
    public float SolarShape(float wavelengthNm, WavelengthLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        return BlackbodyShape(wavelengthNm, SunTempK) * lookup.BlackbodyLumaNorm(SunTempK);
    }

    /// <summary>
    /// <c>(550/λ)⁴</c>, the Rayleigh coefficient relative to 550 nm.
    /// </summary>
    public static float RayleighRelative(float wavelengthNm)
    {
        float r = ReferenceNm / MathF.Max(wavelengthNm, 1f);
        return r * r * r * r;
    }

    /// <summary>
    /// Dome radiance toward <paramref name="dir"/> (the ray's travel direction) at a wavelength with solar
    /// shape <paramref name="solarShape"/> and Rayleigh coefficient <paramref name="rayleighRelative"/> — the
    /// sky without the sun disk. The integrator evaluates exactly this per companion wavelength.
    /// </summary>
    public float DomeRadiance(Vector3 dir, float solarShape, float rayleighRelative)
    {
        Vector3 d = Vector3.Normalize(dir);
        float cosSun = Vector3.Dot(d, ToSun);
        float cosZenith = Math.Clamp(d.Y, 0f, 1f);
        float airmass = 1f / (cosZenith + AirmassFloor);
        float tau = RayleighDepth * rayleighRelative * airmass;
        float inscatter = 1f - MathF.Exp(-tau);
        float phase = 0.75f * (1f + cosSun * cosSun);
        return SkyStrength * solarShape * phase * inscatter;
    }

    /// <summary>Sun irradiance at normal incidence at a wavelength with solar shape <paramref name="solarShape"/>.</summary>
    public float SunSpectralIrradiance(float solarShape) => SunIrradiance * solarShape;

    /// <summary>
    /// Radiance of the sun disk at a wavelength with solar shape <paramref name="solarShape"/>: the uniform
    /// radiance whose irradiance over <see cref="SunSolidAngle"/> is <see cref="SunSpectralIrradiance"/>.
    /// </summary>
    public float SunDiskRadiance(float solarShape) => SunSpectralIrradiance(solarShape) / SunSolidAngle;

    private static float BlackbodyShape(float wavelengthNm, float tempK)
    {
        const float c2NmK = 1.438777e7f;
        float r = ReferenceNm / wavelengthNm;
        float denom = MathF.Exp(c2NmK / (wavelengthNm * tempK)) - 1f;
        float denomRef = MathF.Exp(c2NmK / (ReferenceNm * tempK)) - 1f;
        return r * r * r * r * r * (denomRef / denom);
    }
}
