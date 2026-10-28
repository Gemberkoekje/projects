using System.Globalization;
using Vista.Output;
using Vista.Scene;
using Vista.World;

namespace Vista.Tweak;

/// <summary>
/// The window's camera tools (vista-plan §3.1, §10 M1.16): modest by design ("the generator put a bush in the wrong place — move five
/// feet left"). A nudge moves the camera in metres along its own heading, to its right and up, whatever its pitch and roll; a turn pans
/// about the vertical, tilts and rolls in degrees. Neither checks the ground: <see cref="CameraGround.Clamp"/> does.
/// </summary>
public static class CameraMoves
{
    /// <summary>The steepest tilt the window allows, degrees: the camera file refuses ±90.</summary>
    public const double MaxPitchDegrees = 89.0;

    /// <summary>
    /// <paramref name="camera"/> moved <paramref name="forwardMetres"/> along its heading, <paramref name="rightMetres"/> to its right
    /// (both level, whatever the pitch and roll) and <paramref name="upMetres"/> up.
    /// </summary>
    public static CameraSpec Nudge(CameraSpec camera, double forwardMetres, double rightMetres, double upMetres)
    {
        ArgumentNullException.ThrowIfNull(camera);
        (double sy, double cy) = Math.SinCos(camera.YawDegrees * Math.PI / 180.0);
        WorldPoint p = camera.Position;
        return camera with
        {
            Position = new WorldPoint(p.X + sy * forwardMetres + cy * rightMetres, p.Y + upMetres, p.Z + cy * forwardMetres - sy * rightMetres),
        };
    }

    /// <summary>
    /// <paramref name="camera"/> panned <paramref name="panDegrees"/> clockwise seen from above (the heading, kept in [0, 360)), tilted
    /// <paramref name="tiltDegrees"/> up (the pitch, kept inside ±<see cref="MaxPitchDegrees"/>) and rolled <paramref name="rollDegrees"/>
    /// to the left (kept in (−180, 180]).
    /// </summary>
    public static CameraSpec Turn(CameraSpec camera, double panDegrees, double tiltDegrees, double rollDegrees)
    {
        ArgumentNullException.ThrowIfNull(camera);
        double yaw = (camera.YawDegrees + panDegrees) % 360.0;
        if (yaw < 0.0)
            yaw += 360.0;
        double roll = (camera.RollDegrees + rollDegrees) % 360.0;
        if (roll > 180.0)
            roll -= 360.0;
        else if (roll <= -180.0)
            roll += 360.0;
        return camera with
        {
            YawDegrees = yaw,
            PitchDegrees = Math.Clamp(camera.PitchDegrees + tiltDegrees, -MaxPitchDegrees, MaxPitchDegrees),
            RollDegrees = roll,
        };
    }
}

/// <summary>
/// Where the window's camera may stand (vista-plan §3.1: "it cannot end up underground, underwater or inside a tree", §10 M1.16): at
/// least eye height above the world's surface, plus the most the ground's detail rises above the grid there, plus the forest's crowns
/// where forest can grow (below the tree line); and over the world's grid, whose surface it knows. A camera below that floor is lifted to
/// it. There is no water yet (M3), so nothing is underwater.
/// </summary>
/// <param name="Surface">The world's surface, on the world grid.</param>
/// <param name="DetailMetres">The most the ground's detail below the grid rises above it, m (the rock's, the scree's or the smooth ground's).</param>
/// <param name="CanopyMetres">The most the forest's crowns rise above the ground, m; 0 with no forest.</param>
/// <param name="TreeLineHeight">The tree line as a world height (above sea level, less the scene's base altitude), m; the forest grows below it.</param>
public sealed record CameraGround(HeightField Surface, double DetailMetres, double CanopyMetres, double TreeLineHeight)
{
    /// <summary>The eye's height above the floor's ground, m: a person's (<see cref="DefaultCamera.EyeHeightMetres"/>).</summary>
    public const double EyeMetres = DefaultCamera.EyeHeightMetres;

    /// <summary>
    /// The ground of <paramref name="surface"/> with what a run's sidecar says of its ground's <paramref name="materials"/> (their detail and
    /// forest; null for none) and of the scene's base altitude, <paramref name="groundAltitudeMetres"/> (<see cref="AtmosphereInfo.GroundAltitudeMetres"/>).
    /// </summary>
    public static CameraGround Of(HeightField surface, MaterialsInfo? materials, double groundAltitudeMetres)
    {
        ArgumentNullException.ThrowIfNull(surface);
        double detail = materials?.Detail is { } d ? Math.Max(d.RockAmplitudeMetres, Math.Max(d.ScreeAmplitudeMetres, d.SmoothAmplitudeMetres)) : 0.0;
        double canopy = materials is { Forest: true, Detail: { } c } ? c.CanopyAmplitudeMetres : 0.0;
        double treeLine = materials?.TreeLineMetres is double line ? line - groundAltitudeMetres : double.NegativeInfinity;
        return new CameraGround(surface, detail, canopy, treeLine);
    }

    /// <summary>The lowest the eye may stand at (<paramref name="x"/>, <paramref name="z"/>), m.</summary>
    public double FloorAt(double x, double z)
    {
        double ground = Surface.HeightAt(x, z);
        double above = ground < TreeLineHeight ? Math.Max(DetailMetres, CanopyMetres) : DetailMetres;
        return ground + above + EyeMetres;
    }

    /// <summary>
    /// <paramref name="camera"/> kept over the world's grid and lifted to the floor where it stands below it; <paramref name="moved"/>
    /// says why it was moved (null when it was not).
    /// </summary>
    public CameraSpec Clamp(CameraSpec camera, out string? moved)
    {
        ArgumentNullException.ThrowIfNull(camera);
        moved = null;
        WorldPoint p = camera.Position;
        double half = 0.5 * Surface.Extent;
        double x = Math.Clamp(p.X, -half, half), z = Math.Clamp(p.Z, -half, half);
        if (x != p.X || z != p.Z)
            moved = "kept over the world's edge";
        double ground = Surface.HeightAt(x, z);
        double floor = FloorAt(x, z);
        double y = p.Y;
        if (y < floor)
        {
            string what = ground < TreeLineHeight && CanopyMetres > DetailMetres ? "the forest's crowns" : "the ground";
            moved = string.Create(CultureInfo.InvariantCulture, $"lifted {floor - y:0.#} m, clear of {what}");
            y = floor;
        }
        return moved is null ? camera : camera with { Position = new WorldPoint(x, y, z) };
    }
}
