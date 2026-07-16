using System.Numerics;

namespace RayTracer;

/// <summary>The sun's state at a moment in the day/night cycle (outdoor-area-plan.md §O8).</summary>
/// <param name="Direction">Sunlight <b>travel</b> direction (the sun is toward <c>-Direction</c>).</param>
/// <param name="TempK">Blackbody colour temperature (K) — warm near the horizon, daylight when high (§O9).</param>
/// <param name="SunRadiance">Sky sun-disk radiance — fades to 0 at night.</param>
/// <param name="SkyStrength">Rayleigh dome brightness — a tiny starlit floor at night, full by day.</param>
/// <param name="SunLightLevel">Direct sun-light scale in [0,1] — multiplies the directional light's colour so
/// its scene illumination ramps up/down with the sun instead of snapping on/off at the horizon. Reaches 0
/// exactly at the horizon, where the below-horizon light cutoff takes over.</param>
public readonly record struct SunState(Vector3 Direction, float TempK, float SunRadiance, float SkyStrength, float SunLightLevel);

/// <summary>
/// Maps a cycle phase <c>t ∈ [0,1)</c> to the sun's <see cref="SunState"/> for a smooth day → sunset →
/// night → sunrise loop (outdoor-area-plan.md §O8, design.md Phase 11). The sun rides a slightly-tilted
/// circle (east horizon at dawn → overhead at noon → west horizon at dusk → below the horizon at night);
/// as it nears/drops below the horizon the light reddens (colour temperature falls) and both the sun disk
/// and the sky dome dim toward a dark, faintly-lit night. Torches (§O9) are unaffected, so they take over
/// indoors at dusk — the warm/cool balance shifts on its own.
/// </summary>
public static class DayNightCycle
{
    /// <summary>Sun state at cycle phase <paramref name="t"/> (any real; only the fractional part matters).
    /// <c>t=0</c> dawn, <c>0.25</c> noon, <c>0.5</c> dusk, <c>0.75</c> deep night.</summary>
    public static SunState At(float t)
    {
        float theta = (t - MathF.Floor(t)) * MathF.Tau; // 0 = dawn

        // Sun on a tilted circle: dawn (east, +X, low) → noon (+Y, overhead) → dusk (west, −X, low) → night.
        Vector3 toSun = Vector3.Normalize(new Vector3(MathF.Cos(theta), MathF.Sin(theta), 0.30f));
        float height = toSun.Y; // >0 above the horizon (day), <0 below (night)

        // Day factor: 0 at/below the horizon, ramping to full daylight as the sun climbs. It reaches 0
        // exactly at height = 0 so it lines up with the below-horizon light cutoff (Light.SampleShadowRay
        // and the shader's toSun.y <= 0 gate). Everything sun-driven fades through this factor, so the
        // scene brightens/darkens smoothly through dawn/dusk instead of the sun's direct light snapping
        // from ~full to nothing the instant it crosses the horizon (the visible on/off pop).
        float day = SmoothStep(0f, 0.20f, height);
        // Colour temperature: warm ~2400 K at the horizon (sunrise/sunset), daylight ~5800 K when high.
        float tempK = float.Lerp(2400f, 5800f, SmoothStep(0f, 0.45f, height));
        float sunRadiance = 9f * day;
        float skyStrength = float.Lerp(0.015f, 0.7f, day); // tiny starlit floor at night → full by day

        return new SunState(-toSun, tempK, sunRadiance, skyStrength, day);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
