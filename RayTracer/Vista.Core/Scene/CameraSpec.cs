using System.Numerics;

namespace Vista.Scene;

/// <summary>A world-space point in metres. Double precision: Vista worlds are tens of kilometres across.</summary>
public readonly record struct WorldPoint(double X, double Y, double Z)
{
    /// <summary>Single-precision copy for the GPU.</summary>
    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
}

/// <summary>
/// A Vista camera (vista-plan §3.1 / §5.6): where it stands, where it looks, and the lens. Serialized as the
/// <c>--camera c.json</c> file of the headless CLI (§7.4), so a recipe + seed + camera reproduces an image.
///
/// <para><b>World axes</b> (the engine's): metres, <c>+X</c> east, <c>+Y</c> up, <c>+Z</c> north. Camera
/// space matches the engine's primary-ray convention — <c>+X</c> right, <c>+Y</c> up, <c>+Z</c> forward —
/// so <see cref="ToEngineRotation"/> drops straight into <c>RayTracer.Camera.Rotation</c>.</para>
///
/// <para><b>Angles</b> in degrees: <see cref="YawDegrees"/> is the compass heading (0 = north, 90 = east);
/// <see cref="PitchDegrees"/> is positive looking up; <see cref="RollDegrees"/> is positive banking left
/// (the camera's right side rises, counter-clockwise as seen from behind).</para>
/// </summary>
public sealed record CameraSpec
{
    /// <summary>The camera file format version this build reads and writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>File format version; must equal <see cref="CurrentSchema"/>.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>Eye position in world metres.</summary>
    public required WorldPoint Position { get; init; }

    /// <summary>Compass heading in degrees (0 = north / +Z, 90 = east / +X).</summary>
    public double YawDegrees { get; init; }

    /// <summary>Elevation of the view direction in degrees (positive = up). Must lie strictly inside ±90.</summary>
    public double PitchDegrees { get; init; }

    /// <summary>Bank angle in degrees (positive = banking left).</summary>
    public double RollDegrees { get; init; }

    /// <summary>Focal length in millimetres — the zoom.</summary>
    public double FocalLengthMm { get; init; } = 35.0;

    /// <summary>Sensor height in millimetres (24 = full frame); with the focal length it sets the field of view.</summary>
    public double SensorHeightMm { get; init; } = 24.0;

    /// <summary>Aperture as an f-number; 0 = pinhole (everything sharp). Depth of field is not in integrator v0.</summary>
    public double FStop { get; init; }

    /// <summary>Distance in metres to the plane of sharp focus (used once depth of field is on).</summary>
    public double FocusDistanceMetres { get; init; } = 1000.0;

    /// <summary>Exposure compensation in stops, applied by the display transform; the EXR stays scene-referred.</summary>
    public double ExposureEv { get; init; }

    /// <summary>Vertical field of view in radians: <c>2·atan(sensor / 2f)</c>.</summary>
    public double VerticalFovRadians() => 2.0 * Math.Atan(SensorHeightMm / (2.0 * FocalLengthMm));

    /// <summary>Unit view direction in world space.</summary>
    public Vector3D Forward()
    {
        (double sy, double cy) = Math.SinCos(YawDegrees * DegToRad);
        (double sp, double cp) = Math.SinCos(PitchDegrees * DegToRad);
        return new Vector3D(sy * cp, sp, cy * cp);
    }

    /// <summary>Unit vector toward the right edge of the image, in world space.</summary>
    public Vector3D Right()
    {
        // Unrolled right = horizontal, 90° clockwise of the heading; roll turns it about Forward.
        (double sy, double cy) = Math.SinCos(YawDegrees * DegToRad);
        var flatRight = new Vector3D(cy, 0.0, -sy);
        Vector3D up0 = Vector3D.Cross(Forward(), flatRight);
        (double sr, double cr) = Math.SinCos(RollDegrees * DegToRad);
        return flatRight * cr + up0 * sr;
    }

    /// <summary>Unit vector toward the top edge of the image, in world space.</summary>
    public Vector3D Up() => Vector3D.Cross(Forward(), Right());

    /// <summary>
    /// The engine rotation (<c>RayTracer.Camera.Rotation</c>) that maps camera-space <c>(x right, y up,
    /// z forward)</c> to <see cref="Right"/>, <see cref="Up"/> and <see cref="Forward"/>.
    /// </summary>
    public Quaternion ToEngineRotation() =>
        Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(
            (float)(YawDegrees * DegToRad),
            (float)(-PitchDegrees * DegToRad),
            (float)(RollDegrees * DegToRad)));

    /// <summary>Problems that make this camera unusable (empty when it is valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Schema != CurrentSchema)
            errors.Add($"Unsupported camera schema {Schema}; this build reads schema {CurrentSchema}.");
        if (!double.IsFinite(Position.X) || !double.IsFinite(Position.Y) || !double.IsFinite(Position.Z))
            errors.Add("Camera position must be finite.");
        if (!double.IsFinite(YawDegrees) || !double.IsFinite(RollDegrees))
            errors.Add("Yaw and roll must be finite.");
        if (double.IsNaN(PitchDegrees) || Math.Abs(PitchDegrees) >= 90.0)
            errors.Add("Pitch must lie strictly between -90 and 90 degrees.");
        if (!double.IsFinite(FocalLengthMm) || FocalLengthMm <= 0.0)
            errors.Add("Focal length must be positive.");
        if (!double.IsFinite(SensorHeightMm) || SensorHeightMm <= 0.0)
            errors.Add("Sensor height must be positive.");
        if (!double.IsFinite(FStop) || FStop < 0.0)
            errors.Add("F-stop must be 0 (pinhole) or positive.");
        if (!double.IsFinite(FocusDistanceMetres) || FocusDistanceMetres <= 0.0)
            errors.Add("Focus distance must be positive.");
        if (!double.IsFinite(ExposureEv))
            errors.Add("Exposure must be finite.");
        return errors;
    }

    private const double DegToRad = Math.PI / 180.0;
}

/// <summary>A minimal double-precision 3-vector for camera and terrain maths.</summary>
public readonly record struct Vector3D(double X, double Y, double Z)
{
    /// <summary>Euclidean length.</summary>
    public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>Unit vector in the same direction; zero stays zero.</summary>
    public Vector3D Normalized()
    {
        double l = Length();
        return l > 0.0 ? new Vector3D(X / l, Y / l, Z / l) : this;
    }

    /// <summary>Dot product.</summary>
    public static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Cross product (the plain component formula; the engine's world is left-handed).</summary>
    public static Vector3D Cross(Vector3D a, Vector3D b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>Scale.</summary>
    public static Vector3D operator *(Vector3D v, double s) => new(v.X * s, v.Y * s, v.Z * s);

    /// <summary>Sum.</summary>
    public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Difference.</summary>
    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}
