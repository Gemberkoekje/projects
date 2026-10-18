using Vista.World;

namespace Vista.Scene;

/// <summary>
/// The camera used when the CLI is given none: a stand-in for the auto camera of M1 (§4.8). It stands at eye
/// height on the south side of the world, looking north and slightly up, so the bulk of the terrain is in
/// front of it. Deterministic from the height field alone; it makes no attempt at composition.
/// </summary>
public static class DefaultCamera
{
    /// <summary>Eye height above the ground in metres.</summary>
    public const double EyeHeightMetres = 1.7;

    /// <summary>The default camera for <paramref name="field"/>.</summary>
    public static CameraSpec For(HeightField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        double z = -0.35 * field.Extent;
        double ground = field.HeightAt(0.0, z);
        return new CameraSpec
        {
            Position = new WorldPoint(0.0, ground + EyeHeightMetres, z),
            YawDegrees = 0.0,
            PitchDegrees = 3.0,
        };
    }

    /// <summary>
    /// A warning when <paramref name="camera"/> stands below the terrain surface (until the collision of
    /// the M1 tweak loop exists, the CLI only reports it). Null when the camera is above ground.
    /// </summary>
    public static string? GroundWarning(CameraSpec camera, HeightField field)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(field);
        double ground = field.HeightAt(camera.Position.X, camera.Position.Z);
        return camera.Position.Y <= ground
            ? $"Camera is {ground - camera.Position.Y:0.##} m below the terrain surface ({ground:0.##} m)."
            : null;
    }
}
