using System.Numerics;

namespace Vista.Atmosphere;

/// <summary>
/// One render's atmosphere (vista-plan §10 M1.3): the model, where the scene sits in it and where the sun is.
/// World <c>y = 0</c> is at <see cref="GroundAltitudeMetres"/> above sea level, the scene's base altitude
/// (the place's valley floor until M1.4's altitude control). The ground sphere passes through it: it casts the
/// Earth's shadow and is the land seen beyond the terrain tile, so the valley floor's horizon is flat and its
/// sunset is the almanac's. A point's altitude for the air is the base plus its height; M1.4 adds the Earth's
/// curvature drop across the tile, which this slice ignores (one local vertical, +Y).
///
/// <para>The sky-view LUT has <see cref="AtmosphereLuts.SkySlices"/> slices: slice 0 at the camera's exact
/// altitude, for camera rays, and <see cref="AtmosphereLuts.SkyTerrainSlices"/> spread evenly over the
/// terrain's altitudes, interpolated for rays that leave the terrain after a bounce.</para>
/// </summary>
public sealed record AtmosphereFrame
{
    /// <summary>The smallest height above the ground sphere a slice or lookup uses, m: keeps the horizon maps defined.</summary>
    public const double MinimumHeightAboveGround = 1.0;

    /// <summary>The air.</summary>
    public required AtmosphereModel Model { get; init; }

    /// <summary>Altitude above sea level of world <c>y = 0</c> and of the ground sphere, m.</summary>
    public required double GroundAltitudeMetres { get; init; }

    /// <summary>Unit vector toward the sun in engine axes (+Y up).</summary>
    public required Vector3 ToSun { get; init; }

    /// <summary>Altitude above sea level of the camera (sky-view slice 0), m.</summary>
    public required double CameraAltitudeMetres { get; init; }

    /// <summary>Lowest terrain altitude above sea level (the first terrain slice), m.</summary>
    public required double TerrainLowAltitudeMetres { get; init; }

    /// <summary>Highest terrain altitude above sea level (the last terrain slice), m.</summary>
    public required double TerrainHighAltitudeMetres { get; init; }

    /// <summary>Radius of the sea-level sphere, m.</summary>
    public double SeaLevelRadius => Model.PlanetRadiusMetres;

    /// <summary>Radius of the ground sphere, m.</summary>
    public double GroundRadius => Model.PlanetRadiusMetres + GroundAltitudeMetres;

    /// <summary>Radius of the top of the atmosphere, m.</summary>
    public double TopRadius => Model.TopRadiusMetres;

    /// <summary>Cosine of the sun's zenith angle (the same at every slice: one local vertical).</summary>
    public double SunCosZenith => Math.Clamp(ToSun.Y, -1.0, 1.0);

    /// <summary>The frame for a scene whose world <c>y</c> runs from <paramref name="terrainLowY"/> to <paramref name="terrainHighY"/>.</summary>
    public static AtmosphereFrame For(AtmosphereModel model, double groundAltitude, Vector3 toSun, double cameraY, double terrainLowY, double terrainHighY)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new AtmosphereFrame
        {
            Model = model,
            GroundAltitudeMetres = groundAltitude,
            ToSun = toSun.LengthSquared() > 1e-12f ? Vector3.Normalize(toSun) : Vector3.UnitY,
            CameraAltitudeMetres = groundAltitude + cameraY,
            TerrainLowAltitudeMetres = groundAltitude + terrainLowY,
            TerrainHighAltitudeMetres = groundAltitude + Math.Max(terrainLowY, terrainHighY),
        };
    }

    /// <summary>Altitude above sea level of sky-view slice <paramref name="slice"/>, clamped a metre above the ground sphere.</summary>
    public double SliceAltitude(int slice)
    {
        if (slice < 0 || slice >= AtmosphereLuts.SkySlices)
            throw new ArgumentOutOfRangeException(nameof(slice), slice, "No such sky-view slice.");
        double altitude = slice == 0
            ? CameraAltitudeMetres
            : TerrainLowAltitudeMetres + (TerrainHighAltitudeMetres - TerrainLowAltitudeMetres) * (slice - 1) / (AtmosphereLuts.SkyTerrainSlices - 1);
        return ClampAltitude(altitude);
    }

    /// <summary>
    /// True when some point of the scene (the camera or the highest terrain) can see part of the sun's disk over
    /// the ground sphere's horizon, so next-event estimation on the sun is worth its shadow rays.
    /// </summary>
    public bool SunReachesScene
    {
        get
        {
            double highest = Math.Max(CameraAltitudeMetres, TerrainHighAltitudeMetres) - GroundAltitudeMetres;
            highest = Math.Max(highest, MinimumHeightAboveGround);
            double dip = Math.Atan2(Math.Sqrt(highest * (2.0 * GroundRadius + highest)), GroundRadius);
            return Math.Asin(SunCosZenith) + SolarSpectrum.AngularRadiusRadians > -dip;
        }
    }

    /// <summary><paramref name="altitude"/> kept at least <see cref="MinimumHeightAboveGround"/> above the ground sphere and inside the air.</summary>
    public double ClampAltitude(double altitude) =>
        Math.Clamp(altitude, GroundAltitudeMetres + MinimumHeightAboveGround, Model.TopAltitudeMetres - MinimumHeightAboveGround);

    /// <summary>Problems that make this frame unusable (empty when it is valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>(Model.Validate());
        if (!double.IsFinite(GroundAltitudeMetres) || GroundAltitudeMetres < 0.0 || GroundAltitudeMetres > 10_000.0)
            errors.Add("The ground altitude must be in [0, 10 000] m above sea level.");
        if (!double.IsFinite(CameraAltitudeMetres) || !double.IsFinite(TerrainLowAltitudeMetres) || !double.IsFinite(TerrainHighAltitudeMetres))
            errors.Add("Altitudes must be finite.");
        return errors;
    }
}
