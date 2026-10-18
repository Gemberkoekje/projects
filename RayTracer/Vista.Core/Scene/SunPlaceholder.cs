using System.Numerics;
using RayTracer;
using Vista.Recipes;

namespace Vista.Scene;

/// <summary>
/// The M0 sun (vista-plan §8.1: the engine's <see cref="DayNightCycle"/> and <see cref="Sky"/> are M0
/// placeholders only). Each time-of-day block picks a phase on the maze's day/night circle — east at dawn,
/// west at dusk — and the integrator's sky is the engine's single-scattering Rayleigh <see cref="Sky"/>
/// built from the returned state. Real astronomy (latitude, date, local solar time; §3.5) and the full
/// atmosphere (§5.2) replace both in M1. The placeholder sky cannot do twilight, so dawn and dusk sit a few
/// degrees <i>above</i> the horizon rather than before sunrise / after sunset.
/// </summary>
public static class SunPlaceholder
{
    /// <summary>Stable description recorded in the sidecar.</summary>
    public const string Source = "DayNightCycle placeholder (M0)";

    /// <summary>The <see cref="DayNightCycle"/> phase used for <paramref name="time"/>.</summary>
    public static float PhaseFor(TimeOfDay time) => time switch
    {
        TimeOfDay.Dawn => 0.01f,       // ~3° above the eastern horizon
        TimeOfDay.Day => 0.25f,        // highest point of the cycle
        TimeOfDay.GoldenHour => 0.47f, // ~10° above the western horizon
        TimeOfDay.Dusk => 0.49f,       // ~3° above the western horizon
        TimeOfDay.Night => 0.75f,      // deepest night
        _ => throw new ArgumentOutOfRangeException(nameof(time), time, "Unknown time of day."),
    };

    /// <summary>The sun state for <paramref name="time"/>.</summary>
    public static SunState For(TimeOfDay time) => DayNightCycle.At(PhaseFor(time));

    /// <summary>Elevation of the sun above the horizon in degrees (negative below it).</summary>
    public static double ElevationDegrees(SunState sun)
    {
        Vector3 toSun = -Vector3.Normalize(sun.Direction);
        return Math.Asin(Math.Clamp(toSun.Y, -1f, 1f)) * (180.0 / Math.PI);
    }
}
