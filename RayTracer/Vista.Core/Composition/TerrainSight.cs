using Vista.Scene;
using Vista.World;

namespace Vista.Composition;

/// <summary>What the ground is at a place, for <see cref="TerrainSight"/>'s buffers.</summary>
public interface IGroundClassifier
{
    /// <summary>
    /// The class of the ground at world (<paramref name="x"/>, <paramref name="z"/>), <paramref name="height"/> m above the world's
    /// height 0, whose unit normal's horizontal components are <paramref name="normalX"/> and <paramref name="normalZ"/>.
    /// </summary>
    GroundClass At(double x, double z, double height, double normalX, double normalZ);
}

/// <summary>
/// What <see cref="CompositionScorer"/> asks of the terrain beyond a buffer: whether the sun reaches the ground at a place (the light
/// score, the detail budget) and how fine the terrain's structure is there (the detail budget).
/// </summary>
public interface ITerrainProbe
{
    /// <summary>
    /// True when the sun, in direction <paramref name="toSun"/> (world axes), lights the ground at world (<paramref name="x"/>,
    /// <paramref name="z"/>) whose unit normal is <paramref name="normal"/>: the ground faces it and nothing stands in the way.
    /// </summary>
    bool Lit(double x, double z, Vector3D normal, Vector3D toSun);

    /// <summary>
    /// The finest structure the terrain carries at world (<paramref name="x"/>, <paramref name="z"/>), m: the grid its shape comes from
    /// (the detail below it near the camera is bumps of a metre or two, no stand-in for the ribs, ledges and gullies of 5–50 m a lit
    /// face shows); NaN where the budget does not look (beyond the world, whose base terrain the haze already softens).
    /// </summary>
    double DetailMetres(double x, double z);
}

/// <summary>
/// The class the scene's material rules give a place (vista-plan §10 M1.12–M1.13): the rules' weights from the world's material field
/// under the scene's constants (its grass line, tree line, snow and glaciers), folded by <see cref="GroundClasses.OfWeights"/>. The same
/// rules the integrator runs per hit, without its sub-grid noise; beyond the world the field's maps read as their defaults, as on the GPU.
/// </summary>
/// <param name="Field">The world's material field.</param>
/// <param name="Constants">The scene's material constants (<see cref="MaterialField.Apply"/>d to the field).</param>
/// <param name="GroundAltitudeMetres">The altitude of the world's height 0.</param>
public sealed record MaterialClassifier(MaterialField Field, MaterialConstants Constants, float GroundAltitudeMetres) : IGroundClassifier
{
    /// <inheritdoc/>
    public GroundClass At(double x, double z, double height, double normalX, double normalZ)
    {
        MaterialConstants k = Constants;
        MaterialInputs inputs = Field.Inputs((float)x, (float)z, (float)height, (float)normalX, (float)normalZ, GroundAltitudeMetres, k);
        return GroundClasses.OfWeights(MaterialRules.Evaluate(inputs, k));
    }
}

/// <summary>
/// The terrain as the auto camera sees it (vista-plan §10 M1.14): a CPU tracer over the eroded world grid and the far field's base
/// terrain around it, bent onto the Earth as the renderer's mesh is (<see cref="EarthFrame"/>: a point <c>s</c> away along the ground
/// sinks by about <c>s²/2R</c>), that traces a camera's <see cref="CompositionBuffer"/> at a low resolution. Scoring a few hundred
/// candidate cameras this way needs no mesh and no acceleration structure per camera, runs anywhere (a dry run, CI), and leaves the
/// GPU, which is the user's own (vista-plan §10 M1.13b), alone; the finalists are rendered for real.
///
/// <para>A ray steps over the bilinear surface by half its clearance above it (at least <see cref="MinStepMetres"/>, and a growing share
/// of its distance far out), then finds the crossing by bisection: a slope steeper than 63° could slip between two steps, which the
/// eroded world's 50° rock never is. Beyond the far field's reach a ray still below the highest ground is taken to end there, on ground
/// it would have met further out; above it, it reaches the sky. The world's outer 2 km, where the renderer's mesh blends into the far
/// field, take the world's heights here.</para>
///
/// <para>Thread-safe: every method only reads.</para>
/// </summary>
public sealed class TerrainSight : ITerrainProbe
{
    /// <summary>How far a ray toward the sun looks for the terrain's shadow, m.</summary>
    public const double ShadowReachMetres = 30_000.0;

    /// <summary>The least cosine between the ground's normal and the sun for the ground to count as lit (the sun 6° above it).</summary>
    public const double LitCosine = 0.1;

    /// <summary>The shortest step a ray takes, m: an eighth of the world grid's 8 m.</summary>
    public const double MinStepMetres = 1.0;

    /// <summary>A step's length as a share of the ray's clearance above the ground.</summary>
    public const double StepFraction = 0.5;

    /// <summary>A step's shortest length as a share of the distance already travelled (5 m at 10 km).</summary>
    public const double DistanceStepFraction = 0.0005;

    /// <summary>Where a ray starts, m in front of the lens.</summary>
    public const double StartMetres = 0.25;

    private readonly HeightField _world;
    private readonly HeightField? _far;
    private readonly IGroundClassifier? _classes;
    private readonly PoolMap? _floors;
    private readonly double _top;

    /// <summary>
    /// The terrain of <paramref name="world"/>, with <paramref name="far"/> (the base terrain around it, centred alike; null for the world
    /// alone), bent onto a ground sphere of <paramref name="groundRadius"/>; <paramref name="classes"/> names the ground (all rock
    /// without), and <paramref name="floors"/> gives each place's valley floor (the rise unknown without).
    /// </summary>
    public TerrainSight(HeightField world, HeightField? far, double groundRadius, IGroundClassifier? classes = null, PoolMap? floors = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!(groundRadius > 0.0))
            throw new ArgumentOutOfRangeException(nameof(groundRadius), groundRadius, "The ground sphere needs a positive radius.");
        _world = world;
        _far = far;
        GroundRadius = groundRadius;
        _classes = classes;
        _floors = floors;
        _top = Math.Max(world.MaxHeight, far?.MaxHeight ?? double.NegativeInfinity);
    }

    /// <summary>The world grid.</summary>
    public HeightField World => _world;

    /// <summary>The ground sphere's radius, m.</summary>
    public double GroundRadius { get; }

    /// <summary>The valley floors, when known.</summary>
    public PoolMap? Floors => _floors;

    /// <summary>The highest ground anywhere, m.</summary>
    public double Top => _top;

    /// <summary>The ground's height at world (<paramref name="x"/>, <paramref name="z"/>), m: the world grid's inside its tile, the far field's around it, NaN beyond.</summary>
    public double GroundAt(double x, double z)
    {
        if (Inside(_world, x, z))
            return _world.HeightAt(x, z);
        if (_far is not null && Inside(_far, x, z))
            return _far.HeightAt(x, z);
        return double.NaN;
    }

    /// <summary>The horizontal components of the ground's unit normal at (<paramref name="x"/>, <paramref name="z"/>), from central differences over its grid's cell.</summary>
    public (double X, double Z) NormalAt(double x, double z)
    {
        double e = Inside(_world, x, z) || _far is null ? _world.CellSize : _far.CellSize;
        double dx = (Height(x + e, z) - Height(x - e, z)) / (2.0 * e);
        double dz = (Height(x, z + e) - Height(x, z - e)) / (2.0 * e);
        double inv = 1.0 / Math.Sqrt(dx * dx + 1.0 + dz * dz);
        return (-dx * inv, -dz * inv);

        double Height(double px, double pz)
        {
            double h = GroundAt(px, pz);
            return double.IsNaN(h) ? GroundAt(x, z) : h;
        }
    }

    /// <summary>
    /// The ground's slope at (<paramref name="x"/>, <paramref name="z"/>) in degrees.
    /// </summary>
    public double SlopeAt(double x, double z)
    {
        (double nx, double nz) = NormalAt(x, z);
        double horizontal = Math.Sqrt(nx * nx + nz * nz);
        return Math.Asin(Math.Min(1.0, horizontal)) * (180.0 / Math.PI);
    }

    /// <summary>The valley floor under (<paramref name="x"/>, <paramref name="z"/>), m; NaN where unknown.</summary>
    public double FloorAt(double x, double z) => _floors is null ? double.NaN : CompositionBuffer.FloorAt(_floors, x, z);

    /// <summary>
    /// How far a ray from <paramref name="origin"/> along unit <paramref name="d"/> (world axes, at the origin) goes before it meets the
    /// ground, m; +∞ when it reaches the sky or passes <paramref name="maxDistance"/>. A ray that leaves the terrain still below its
    /// highest ground ends there (on ground further out), unless <paramref name="pastTerrainIsGround"/> is false: a shadow ray then
    /// counts as reaching the sun.
    /// </summary>
    public double Hit(WorldPoint origin, Vector3D d, double maxDistance = double.PositiveInfinity, bool pastTerrainIsGround = true)
    {
        double horizontal = Math.Sqrt(d.X * d.X + d.Z * d.Z);
        double r2 = 2.0 * GroundRadius;
        double t = StartMetres, before = 0.0;
        for (int step = 0; step < 200_000; step++)
        {
            if (t > maxDistance)
                return double.PositiveInfinity;
            double x = origin.X + d.X * t, z = origin.Z + d.Z * t;
            double s = horizontal * t;
            double h = origin.Y + d.Y * t + s * s / r2;
            double ground = GroundAt(x, z);
            if (double.IsNaN(ground))
                return pastTerrainIsGround && h < _top && t > StartMetres ? t : double.PositiveInfinity; // past the far field: ground further out, or the sky
            double clearance = h - ground;
            if (clearance <= 0.0)
                return step == 0 ? t : Crossing(origin, d, horizontal, before, t);
            // Above the highest ground and rising against the curved ground, a ray never comes down to it.
            if (h > _top && d.Y + s * horizontal / GroundRadius >= 0.0)
                return double.PositiveInfinity;
            before = t;
            t += Math.Max(Math.Max(MinStepMetres, t * DistanceStepFraction), StepFraction * clearance);
        }
        return double.PositiveInfinity;
    }

    /// <summary>
    /// <inheritdoc/> Here: the normal faces the sun by at least <see cref="LitCosine"/>, and a ray from a metre above the ground toward
    /// the sun meets no terrain within <see cref="ShadowReachMetres"/>.
    /// </summary>
    public bool Lit(double x, double z, Vector3D normal, Vector3D toSun)
    {
        if (Vector3D.Dot(normal, toSun) < LitCosine)
            return false;
        double ground = GroundAt(x, z);
        if (double.IsNaN(ground))
            return true;
        return double.IsPositiveInfinity(Hit(new WorldPoint(x, ground + 1.0, z), toSun, ShadowReachMetres, pastTerrainIsGround: false));
    }

    /// <summary><inheritdoc/> Here: the world grid's cell inside the world; NaN beyond it.</summary>
    public double DetailMetres(double x, double z) => Inside(_world, x, z) ? _world.CellSize : double.NaN;

    /// <summary>
    /// True when nothing stands between <paramref name="from"/> and <paramref name="to"/> but the last <paramref name="toleranceMetres"/>
    /// before it (the target's own slope). Heights are above the world's height 0; the ground's curvature lowers the far point.
    /// </summary>
    public bool Sees(WorldPoint from, WorldPoint to, double toleranceMetres)
    {
        (Vector3D d, double distance) = Toward(from, to);
        return double.IsPositiveInfinity(Hit(from, d, distance - toleranceMetres));
    }

    /// <summary>
    /// The unit direction from <paramref name="from"/> to <paramref name="to"/> as a camera at <paramref name="from"/> sees it (the far
    /// point lowered by the curvature drop), and the distance.
    /// </summary>
    public (Vector3D Direction, double Distance) Toward(WorldPoint from, WorldPoint to)
    {
        double dx = to.X - from.X, dz = to.Z - from.Z;
        double s2 = dx * dx + dz * dz;
        double dy = to.Y - s2 / (2.0 * GroundRadius) - from.Y;
        var v = new Vector3D(dx, dy, dz);
        double length = v.Length();
        return (length > 0.0 ? v * (1.0 / length) : new Vector3D(0.0, 0.0, 1.0), length);
    }

    /// <summary>
    /// The composition buffer of <paramref name="camera"/> at <paramref name="width"/>×<paramref name="height"/>: one ray through each
    /// pixel's centre. Rows run in parallel when <paramref name="parallel"/>; a search that traces many cameras at once does not.
    /// </summary>
    public CompositionBuffer Trace(CameraSpec camera, int width, int height, bool parallel = false)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var buffer = new CompositionBuffer(width, height);
        var rays = new CameraRays(camera, width, height);
        WorldPoint origin = camera.Position;
        void Row(int y)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3D d = rays.Direction(x, y);
                double t = Hit(origin, d);
                if (!double.IsFinite(t))
                    continue;
                int p = y * width + x;
                double wx = origin.X + d.X * t, wz = origin.Z + d.Z * t;
                double ground = GroundAt(wx, wz);
                if (double.IsNaN(ground))
                {
                    // Past the far field: distant ground, seen level.
                    buffer.Depth[p] = (float)t;
                    buffer.Class[p] = GroundClass.Rock;
                    buffer.Normal[p * 3 + 1] = 1f;
                    continue;
                }
                (double nx, double nz) = NormalAt(wx, wz);
                buffer.Depth[p] = (float)t;
                buffer.Normal[p * 3] = (float)nx;
                buffer.Normal[p * 3 + 1] = (float)Math.Sqrt(Math.Max(0.0, 1.0 - nx * nx - nz * nz));
                buffer.Normal[p * 3 + 2] = (float)nz;
                buffer.Class[p] = _classes?.At(wx, wz, ground, nx, nz) ?? GroundClass.Rock;
                double floor = FloorAt(wx, wz);
                buffer.Rise[p] = double.IsNaN(floor) ? float.NaN : (float)(ground - floor);
            }
        }
        if (parallel)
        {
            Parallel.For(0, height, Row);
        }
        else
        {
            for (int y = 0; y < height; y++)
                Row(y);
        }
        return buffer;
    }

    /// <summary>The crossing between <paramref name="above"/> (clear) and <paramref name="below"/> (in the ground), by bisection.</summary>
    private double Crossing(WorldPoint origin, Vector3D d, double horizontal, double above, double below)
    {
        double r2 = 2.0 * GroundRadius;
        for (int i = 0; i < 10; i++)
        {
            double t = 0.5 * (above + below);
            double s = horizontal * t;
            double ground = GroundAt(origin.X + d.X * t, origin.Z + d.Z * t);
            if (!double.IsNaN(ground) && origin.Y + d.Y * t + s * s / r2 <= ground)
                below = t;
            else
                above = t;
        }
        return below;
    }

    private static bool Inside(HeightField field, double x, double z) =>
        x >= field.OriginX && z >= field.OriginZ && x <= field.OriginX + field.Extent && z <= field.OriginZ + field.Extent;
}
