using System.Diagnostics;
using Vista.Scene;

namespace Vista.World;

/// <summary>Which terrain mesh a render gets.</summary>
public enum MeshKind
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>The view-dependent mesh (vista-plan §10 M1.9): refined to an error in pixels, with the far field and the detail.</summary>
    View,

    /// <summary>Every sample of the world grid at full resolution, nothing beyond it, no detail (M1.7's mesh, bent): for comparison.</summary>
    Uniform,
}

/// <summary>How the terrain is meshed (vista-plan §10 M1.9).</summary>
public sealed record MeshSettings
{
    /// <summary>The default error target, in pixels at the frame's centre.</summary>
    public const double DefaultErrorPixels = 1.0;

    /// <summary>
    /// The default error target off screen, as an angle seen from the camera: half the sun's angular radius. Geometry off screen
    /// matters through the shadows it casts on screen, and the sun's disk blurs a shadow's edge over twice its angular radius
    /// times the distance from the occluder, so an occluder this far off moves the edge by at most a quarter of its blur.
    /// </summary>
    public const double DefaultOffScreenRadians = 0.5 * Atmosphere.SolarSpectrum.AngularRadiusRadians;

    /// <summary>The view-dependent mesh or the uniform one.</summary>
    public MeshKind Kind { get; init; } = MeshKind.View;

    /// <summary>
    /// The most a vertex left out may lie off the mesh where the camera sees it, in pixels of the frame's centre at its distance.
    /// </summary>
    public double ErrorPixels { get; init; } = DefaultErrorPixels;

    /// <summary>
    /// The most a vertex left out may lie off the mesh where the camera does not see it, as an angle from the camera, rad; never
    /// finer than <see cref="ErrorPixels"/> would be on screen. Zero meshes off screen exactly as on screen.
    /// </summary>
    public double OffScreenRadians { get; init; } = DefaultOffScreenRadians;

    /// <summary>
    /// The longest a triangle's long edge may be on screen, in pixels of the frame's centre, whatever its error: the smooth surface
    /// below the world grid and the mesh's own normals need triangles small next to a pixel to read as smooth. Only edges up to a
    /// grid cell's diagonal count, so this refines the near field alone; off screen only the error counts. The default is 8.
    /// </summary>
    public double MaxTrianglePixels { get; init; } = 8.0;

    /// <summary>Add the detail below the grid (<see cref="TerrainDetail"/>).</summary>
    public bool Detail { get; init; } = true;

    /// <summary>
    /// The ground's materials by rule (vista-plan §10 M1.12): the detail below the grid follows the material (<see cref="MaterialDetail"/>)
    /// and the mesh carries the world's <see cref="MaterialField"/> for the shader. Off renders M0's single material over M1.9's
    /// smooth detail, as before M1.12.
    /// </summary>
    public bool Materials { get; init; } = true;

    /// <summary>Mesh the far field beyond the world grid, out to the horizon.</summary>
    public bool FarField { get; init; } = true;

    /// <summary>Problems that make these settings unusable (empty when valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Kind is not (MeshKind.View or MeshKind.Uniform))
            errors.Add($"Unknown mesh kind {Kind}.");
        if (!double.IsFinite(ErrorPixels) || ErrorPixels < 0.01 || ErrorPixels > 1000.0)
            errors.Add("The mesh's error target must be between 0.01 and 1000 pixels.");
        if (!double.IsFinite(OffScreenRadians) || OffScreenRadians < 0.0 || OffScreenRadians > 1.0)
            errors.Add("The mesh's error target off screen must be between 0 and 1 rad.");
        if (!double.IsFinite(MaxTrianglePixels) || MaxTrianglePixels < 1.0 || MaxTrianglePixels > 100_000.0)
            errors.Add("The mesh's largest triangle must be between 1 and 100 000 pixels.");
        return errors;
    }
}

/// <summary>
/// What a view-dependent mesh is built for: where the camera stands and looks, the frame's field of view, how many pixels a
/// radian covers at the frame's centre, and the ground sphere the world is bent onto.
/// </summary>
/// <param name="Camera">The camera's world position.</param>
/// <param name="Forward">The camera's forward axis.</param>
/// <param name="Right">The camera's right axis.</param>
/// <param name="Up">The camera's up axis.</param>
/// <param name="TanHalfWidth">tan of half the horizontal field of view.</param>
/// <param name="TanHalfHeight">tan of half the vertical field of view.</param>
/// <param name="PixelsPerRadian">Pixels per radian at the frame's centre: the frame's height over 2·tan(fov/2).</param>
/// <param name="GroundRadius">The ground sphere's radius, m.</param>
public readonly record struct MeshView(
    WorldPoint Camera,
    Vector3D Forward,
    Vector3D Right,
    Vector3D Up,
    double TanHalfWidth,
    double TanHalfHeight,
    double PixelsPerRadian,
    double GroundRadius)
{
    /// <summary>The view of <paramref name="camera"/> on a <paramref name="frameWidth"/>×<paramref name="frameHeight"/> frame.</summary>
    public static MeshView For(CameraSpec camera, int frameWidth, int frameHeight, double groundRadius)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
        double tanHalf = Math.Tan(0.5 * camera.VerticalFovRadians());
        return new MeshView(camera.Position, camera.Forward(), camera.Right(), camera.Up(), tanHalf * frameWidth / frameHeight, tanHalf,
            frameHeight / (2.0 * tanHalf), groundRadius);
    }

    /// <summary>The frame the mesh is built in.</summary>
    public EarthFrame Frame => new(GroundRadius, Camera.X, Camera.Z);
}

/// <summary>What a mesh is and what it cost, for the sidecar (schema 10) and the measurements.</summary>
public sealed record MeshInfo
{
    /// <summary>The kind.</summary>
    public required MeshKind Kind { get; init; }

    /// <summary>The error target, px (view meshes).</summary>
    public double ErrorPixels { get; init; }

    /// <summary>Pixels per radian at the frame's centre it was built for.</summary>
    public double PixelsPerRadian { get; init; }

    /// <summary>Whether the detail below the grid was added.</summary>
    public bool Detail { get; init; }

    /// <summary>Whether the ground has its materials by rule, and the detail follows them (schema 17).</summary>
    public bool Materials { get; init; }

    /// <summary>Whether the far field was meshed.</summary>
    public bool FarField { get; init; }

    /// <summary>Triangles.</summary>
    public long Triangles { get; init; }

    /// <summary>Of those, triangles finer than the world grid (the detail).</summary>
    public long DetailTriangles { get; init; }

    /// <summary>Of those, triangles beyond the world grid (the far field).</summary>
    public long FarTriangles { get; init; }

    /// <summary>Vertices.</summary>
    public long Vertices { get; init; }

    /// <summary>Upload size (positions, normals, indices), bytes.</summary>
    public long Bytes { get; init; }

    /// <summary>How far the mesh reaches from the camera, m: its horizon reach with the far field, else the world grid's farthest corner.</summary>
    public double ReachMetres { get; init; }

    /// <summary>The ground sphere's radius the mesh is bent onto, m.</summary>
    public double GroundRadiusMetres { get; init; }

    /// <summary>The lowest the meshed world can reach above the ground sphere, m.</summary>
    public double MinHeightMetres { get; init; }

    /// <summary>The highest the meshed world can reach above the ground sphere, m.</summary>
    public double MaxHeightMetres { get; init; }

    /// <summary>Time to refine and emit the triangles, s.</summary>
    public double RefineSeconds { get; init; }

    /// <summary>Time to place the vertices (the detail, the bend) and their normals, s.</summary>
    public double VertexSeconds { get; init; }

    /// <summary>Time to build the world's hierarchy, when this mesh had to (once per world), s.</summary>
    public double HierarchySeconds { get; init; }
}

/// <summary>
/// Builds the terrain mesh of a view (vista-plan §4.3, §10 M1.9) from a <see cref="TerrainHierarchy"/>: the 4-8 mesh refined from
/// its root, each triangle split at its long edge's midpoint while the camera would see that vertex's error — its saturated
/// error <c>e</c> over its distance less its radius <c>r</c>, in pixels, above the target <c>τ</c>: <c>e·λ/τ + r &gt; d</c>, with
/// <c>λ</c> the pixels per radian. The target is by distance alone, so geometry off screen keeps the detail it would have on
/// screen at that distance (shadows from a peak behind the camera, reflections later), and a camera that only turns keeps its
/// mesh. Triangles wholly beyond the camera's horizon reach (its horizon plus the highest terrain's) are left out.
///
/// <para>The vertices are then placed in the <see cref="EarthFrame"/> around the camera: the hierarchy's height plus the detail,
/// bent onto the ground sphere. Normals are the angle-weighted mean of the faces around each vertex, so the shading follows the
/// mesh at whatever detail it has there. The traversal is depth first and single threaded, so the triangles and vertices come
/// out in the same order every time (the acceleration structure, and so the image, depend on it); placing the vertices runs in
/// parallel.</para>
/// </summary>
public static class ViewMesher
{
    /// <summary>The horizon reach is padded by this much, for the terrain beyond the horizon that still shows over it.</summary>
    private const double ReachMargin = 1.05;

    /// <summary>The frustum the on-screen target applies in is this much wider than the frame's.</summary>
    private const double FrustumMargin = 1.02;

    /// <summary>
    /// Off screen, the detail below the grid's target is this many times the grid's: the sun's whole disk (four times half its
    /// angular radius) by default.
    /// </summary>
    private const double OffScreenDetailFactor = 4.0;

    /// <summary>The mesh of <paramref name="view"/> under <paramref name="settings"/>.</summary>
    public static TerrainMesh Build(TerrainHierarchy hierarchy, MeshView view, MeshSettings settings)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(settings);
        IReadOnlyList<string> problems = settings.Validate();
        if (problems.Count > 0)
            throw new ArgumentException(string.Join(" ", problems), nameof(settings));
        if (!(view.PixelsPerRadian > 0.0) || !(view.GroundRadius > 0.0))
            throw new ArgumentException("The view needs positive pixels per radian and a ground radius.", nameof(view));

        var stopwatch = Stopwatch.StartNew();
        var refiner = new Refiner(hierarchy, view, settings);
        refiner.Run();
        double refineSeconds = stopwatch.Elapsed.TotalSeconds;
        stopwatch.Restart();

        bool detail = settings.Kind == MeshKind.View && settings.Detail;
        bool far = settings.Kind == MeshKind.View && settings.FarField;
        int vertexCount = refiner.VertexCount;
        var positions = new float[vertexCount * 3];
        bool maps = settings.Kind == MeshKind.View;
        uint[] mapNormals = maps ? new uint[vertexCount] : [];
        EarthFrame frame = view.Frame;
        Parallel.For(0, vertexCount, v =>
        {
            (int i, int j, float baseHeight) = refiner.Vertex(v);
            double x = hierarchy.WorldOf(i), z = hierarchy.WorldOf(j);
            double h = baseHeight + (detail ? hierarchy.Detail.Height(x, z) : 0.0);
            Vector3D p = frame.ToFrame(x, h, z);
            positions[v * 3] = (float)p.X;
            positions[v * 3 + 1] = (float)p.Y;
            positions[v * 3 + 2] = (float)p.Z;
            if (maps)
            {
                (float nx, float nz) = hierarchy.MapNormal(x, z);
                mapNormals[v] = NormalMap.Pack(nx, nz);
            }
        });
        uint[] indices = refiner.Indices();
        float[] normals = Normals(positions, indices, vertexCount);
        double vertexSeconds = stopwatch.Elapsed.TotalSeconds;

        // What the atmosphere is built for: the heights the world can reach and how far the mesh can reach — properties of the
        // camera's place, never of its heading, so a turn rebuilds no sky view.
        double amplitude = detail ? hierarchy.Detail.MaxAmplitude : 0.0;
        double min = (far ? hierarchy.MinHeight : hierarchy.Surface.MinHeight) - amplitude;
        double max = (far ? hierarchy.MaxHeight : hierarchy.Surface.MaxHeight) + amplitude;
        double reach = far ? refiner.Reach : FarthestCorner(hierarchy.Surface, view.Camera);
        var mesh = new TerrainMesh(positions, normals, indices, mapNormals, maps ? hierarchy.NormalMaps : null, hierarchy.Materials)
        {
            Frame = frame,
            MinHeight = min,
            MaxHeight = max,
            ReachMetres = reach,
        };
        mesh.Info = new MeshInfo
        {
            Kind = settings.Kind,
            ErrorPixels = settings.Kind == MeshKind.View ? settings.ErrorPixels : 0.0,
            PixelsPerRadian = view.PixelsPerRadian,
            Detail = detail,
            Materials = hierarchy.Materials is not null,
            FarField = far,
            Triangles = mesh.TriangleCount,
            DetailTriangles = refiner.DetailTriangles,
            FarTriangles = refiner.FarTriangles,
            Vertices = mesh.VertexCount,
            Bytes = mesh.ByteSize,
            ReachMetres = mesh.ReachMetres,
            GroundRadiusMetres = view.GroundRadius,
            MinHeightMetres = min,
            MaxHeightMetres = max,
            RefineSeconds = refineSeconds,
            VertexSeconds = vertexSeconds,
        };
        return mesh;
    }

    /// <summary>The farthest corner of the world grid from <paramref name="camera"/>, horizontally, m.</summary>
    private static double FarthestCorner(HeightField surface, WorldPoint camera)
    {
        double reachX = Math.Max(Math.Abs(surface.OriginX - camera.X), Math.Abs(surface.OriginX + surface.Extent - camera.X));
        double reachZ = Math.Max(Math.Abs(surface.OriginZ - camera.Z), Math.Abs(surface.OriginZ + surface.Extent - camera.Z));
        return Math.Sqrt(reachX * reachX + reachZ * reachZ);
    }

    /// <summary>
    /// A check of the mesh <see cref="Build"/> makes for <paramref name="view"/>: how many of its vertices sit at the midpoint of one
    /// of its triangles' edges — T-junctions, where the mesh would crack (0 when the saturated errors and radii keep the refinement
    /// conforming). Any vertex inside a 4-8 edge implies one at its midpoint, so the midpoints are all that need looking at.
    /// </summary>
    public static int TJunctions(TerrainHierarchy hierarchy, MeshView view, MeshSettings settings)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(settings);
        var refiner = new Refiner(hierarchy, view, settings);
        refiner.Run();
        return refiner.CountTJunctions();
    }

    /// <summary>
    /// Angle-weighted vertex normals: each face's unit normal times its angle at the vertex, summed and normalised. The faces are
    /// worked out in parallel and summed in triangle order, so the result is the same every time.
    /// </summary>
    private static float[] Normals(float[] positions, uint[] indices, int vertexCount)
    {
        int triangles = indices.Length / 3;
        var faces = new float[triangles * 6]; // unit normal, then the angles at its three corners
        Parallel.For(0, triangles, t =>
        {
            Vector3D p0 = Position(positions, indices[t * 3]);
            Vector3D p1 = Position(positions, indices[t * 3 + 1]);
            Vector3D p2 = Position(positions, indices[t * 3 + 2]);
            Vector3D n = Vector3D.Cross(p1 - p0, p2 - p0);
            double length = n.Length();
            if (length == 0.0)
                return;
            int f = t * 6;
            faces[f] = (float)(n.X / length);
            faces[f + 1] = (float)(n.Y / length);
            faces[f + 2] = (float)(n.Z / length);
            faces[f + 3] = (float)Angle(p1 - p0, p2 - p0);
            faces[f + 4] = (float)Angle(p2 - p1, p0 - p1);
            faces[f + 5] = (float)Angle(p0 - p2, p1 - p2);
        });

        var sums = new double[vertexCount * 3];
        for (int t = 0; t < triangles; t++)
        {
            int f = t * 6;
            for (int corner = 0; corner < 3; corner++)
            {
                int v = (int)indices[t * 3 + corner] * 3;
                double w = faces[f + 3 + corner];
                sums[v] += faces[f] * w;
                sums[v + 1] += faces[f + 1] * w;
                sums[v + 2] += faces[f + 2] * w;
            }
        }

        var normals = new float[vertexCount * 3];
        Parallel.For(0, vertexCount, v =>
        {
            int k = v * 3;
            double length = Math.Sqrt(sums[k] * sums[k] + sums[k + 1] * sums[k + 1] + sums[k + 2] * sums[k + 2]);
            if (length > 0.0)
            {
                normals[k] = (float)(sums[k] / length);
                normals[k + 1] = (float)(sums[k + 1] / length);
                normals[k + 2] = (float)(sums[k + 2] / length);
            }
            else
            {
                normals[k + 1] = 1f;
            }
        });
        return normals;
    }

    private static Vector3D Position(float[] positions, uint v) => new(positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2]);

    private static double Angle(Vector3D a, Vector3D b) => Math.Atan2(Vector3D.Cross(a, b).Length(), Vector3D.Dot(a, b));

    /// <summary>The depth-first refinement of one view: the triangles it keeps and the vertices they use, in the order met.</summary>
    private sealed class Refiner
    {
        private readonly TerrainHierarchy _h;
        private readonly bool _uniform;
        private readonly bool _subGrid;
        private readonly bool _farField;
        private readonly double _kOn, _kOff, _kOffDetail, _sizeOn;
        private readonly double[] _edge;
        private readonly double _eyeX, _eyeY, _eyeZ;
        private readonly double _origin, _sub;
        private readonly double _halfInverseRadius;
        private readonly Vector3D _right, _left, _top, _bottom;
        private readonly double _reach;
        private readonly int _g0Low, _g0High;
        private readonly int _subMask;
        private readonly int[][] _storeVertex;
        private readonly VertexTable _otherVertex = new();
        private readonly List<(int I, int J, float Height)> _vertices = new();
        private readonly List<uint> _indices = new();

        public Refiner(TerrainHierarchy hierarchy, MeshView view, MeshSettings settings)
        {
            _h = hierarchy;
            _uniform = settings.Kind == MeshKind.Uniform;
            _subGrid = !_uniform && settings.Detail && !hierarchy.Detail.IsNone;
            _farField = !_uniform && settings.FarField;
            // On screen a pixel is 1/λ rad; off screen the target's angle stands in for τ of them. Errors over τ pixels refine, and
            // on screen so do edges over MaxTrianglePixels pixels.
            double pixel = 1.0 / view.PixelsPerRadian;
            double pixelOff = Math.Max(settings.OffScreenRadians / settings.ErrorPixels, pixel);
            _kOn = 1.0 / (settings.ErrorPixels * pixel);
            _kOff = 1.0 / (settings.ErrorPixels * pixelOff);
            // Off screen, the detail below the grid answers to the sun's whole disk: its shadows are blurred over at least that angle
            // from wherever they fall, so nothing finer shows in them. Coarser than the grid's levels' target, so still monotone.
            _kOffDetail = _kOff / OffScreenDetailFactor;
            // The size rule is about how the smooth surface reads, so only on screen: off screen only the error counts.
            _sizeOn = 1.0 / (settings.MaxTrianglePixels * pixel);
            // The size rule is for the smooth surface below the grid: an edge counts at most as long as a grid cell's diagonal, so
            // only the near field is refined by size (still monotone: the capped length never grows from parent to child).
            _edge = new double[hierarchy.LevelCount];
            double cap = hierarchy.Surface.CellSize * Math.Sqrt(2.0);
            for (int level = 0; level < _edge.Length; level++)
                _edge[level] = Math.Min(hierarchy.EdgeLength(level), cap);
            (_eyeX, _eyeY, _eyeZ) = (view.Camera.X, view.Camera.Y, view.Camera.Z);
            (_origin, _sub) = (hierarchy.RootOrigin, hierarchy.SubUnit);
            _halfInverseRadius = 0.5 / view.GroundRadius;
            // The frustum's four side planes, their normals pointing in, widened by FrustumMargin.
            double tanW = FrustumMargin * view.TanHalfWidth, tanH = FrustumMargin * view.TanHalfHeight;
            _right = (view.Forward * tanW - view.Right).Normalized();
            _left = (view.Forward * tanW + view.Right).Normalized();
            _top = (view.Forward * tanH - view.Up).Normalized();
            _bottom = (view.Forward * tanH + view.Up).Normalized();
            double terrainHigh = hierarchy.MaxHeight + hierarchy.Detail.MaxAmplitude;
            _reach = ReachMargin * view.Frame.HorizonReach(Math.Max(view.Camera.Y, 0.0), Math.Max(terrainHigh, 0.0));
            _g0Low = hierarchy.G0Low;
            _g0High = hierarchy.G0High;
            _subMask = TerrainHierarchy.Sub - 1;
            _storeVertex = new int[3][];
            for (int s = 0; s < _storeVertex.Length; s++)
            {
                _storeVertex[s] = GC.AllocateUninitializedArray<int>(hierarchy.StorePoints(s));
                Array.Fill(_storeVertex[s], -1);
            }
        }

        public int VertexCount => _vertices.Count;

        /// <summary>How far from the camera triangles are kept (the far field's horizon reach), m.</summary>
        public double Reach => _reach;

        public long DetailTriangles { get; private set; }

        public long FarTriangles { get; private set; }

        public (int I, int J, float Height) Vertex(int v) => _vertices[v];

        public uint[] Indices() => [.. _indices];

        public void Run()
        {
            int n = _h.RootSize;
            V Corner(int i, int j)
            {
                _h.TryStored(i, j, out float height, out _, out _);
                return new V(i, j, height);
            }
            V c00 = Corner(0, 0), cn0 = Corner(n, 0), c0n = Corner(0, n), cnn = Corner(n, n);
            // The root's diagonal joins (0, 0) and (n, n); both triangles wind so their faces point up.
            Refine(cn0, c00, cnn);
            Refine(c0n, cnn, c00);
        }

        private readonly record struct V(int I, int J, float H);

        private void Refine(V a, V l, V r)
        {
            int si = l.I + r.I, sj = l.J + r.J;
            if (_farField)
            {
                // Beyond the reach? Every point of the triangle lies within half its long edge of that edge's midpoint.
                int di = Math.Abs(r.I - l.I), dj = Math.Abs(r.J - l.J);
                double half = (di == 0 || dj == 0 ? 0.5 * (di + dj) : 0.70710678118654752 * di) * _sub;
                double dx = _origin + 0.5 * si * _sub - _eyeX, dz = _origin + 0.5 * sj * _sub - _eyeZ;
                double limit = _reach + half;
                if (dx * dx + dz * dz > limit * limit)
                    return;
            }
            else if (!OverlapsG0(a, l, r))
            {
                return;
            }

            if (((si | sj) & 1) != 0)
            {
                Emit(a, l, r);
                return;
            }
            int mi = si >> 1, mj = sj >> 1;
            bool onGrid = ((mi | mj) & _subMask) == 0;
            if (!onGrid && !_subGrid)
            {
                Emit(a, l, r);
                return;
            }

            float height;
            bool active;
            (int t, bool diagonal) = TerrainHierarchy.LevelOf(mi, mj);
            int level = _h.LevelIndex(t, diagonal);
            if (_h.TryStored(mi, mj, out height, out float error, out float radius))
            {
                active = _uniform ? onGrid : Active(error, radius, _edge[level], onGrid, mi, mj, height);
            }
            else
            {
                float midpoint = (l.H + r.H) * 0.5f;
                height = onGrid ? midpoint : _h.SubGridHeight(mi, mj, midpoint);
                // Below the grid the detail's bound is its region's (M1.12: a meadow's smooth detail, not the rock's).
                float bound = t < TerrainHierarchy.SubLevels ? _h.SubGridError(mi, mj, level, t, diagonal) : _h.AnalyticError(level);
                active = _uniform ? onGrid : Active(bound, _h.AnalyticRadius(level), _edge[level], onGrid, mi, mj, height);
            }

            if (!active)
            {
                Emit(a, l, r);
                return;
            }
            var m = new V(mi, mj, height);
            Refine(m, a, l);
            Refine(m, r, a);
        }

        /// <summary>
        /// Whether the camera needs vertex (i, j): its error over its distance less its radius above the target, the on-screen
        /// one when its descendants' sphere reaches into the frustum, else the off-screen one; on screen also when the edge it
        /// splits is longer than the largest triangle. Saturated errors, edges that halve level by level and nested spheres keep
        /// this monotone: a child's sphere inside the frustum puts its parent's there too.
        /// </summary>
        private bool Active(float error, float radius, double edge, bool onGrid, int i, int j, float height)
        {
            double dx = _origin + i * _sub - _eyeX, dy = height - _eyeY, dz = _origin + j * _sub - _eyeZ;
            double horizontal2 = dx * dx + dz * dz;
            double distance2 = horizontal2 + dy * dy;
            double off = error * (onGrid ? _kOff : _kOffDetail) + radius;
            if (off * off > distance2)
                return true;
            double on = Math.Max(error * _kOn, edge * _sizeOn) + radius;
            if (on * on <= distance2)
                return false;
            // In the frustum? Seen bent onto the Earth: the drop s²/2R lowers it, and across the sphere the drop varies by at most
            // (2s·r + r²)/2R, which widens it — enough that a child's widened sphere stays inside its parent's.
            var p = new Vector3D(dx, dy - horizontal2 * _halfInverseRadius, dz);
            double r = -(radius + (2.0 * Math.Sqrt(horizontal2) * radius + radius * radius) * _halfInverseRadius);
            return Vector3D.Dot(p, _right) >= r && Vector3D.Dot(p, _left) >= r && Vector3D.Dot(p, _top) >= r && Vector3D.Dot(p, _bottom) >= r;
        }

        private bool OverlapsG0(V a, V l, V r)
        {
            int minI = Math.Min(a.I, Math.Min(l.I, r.I)), maxI = Math.Max(a.I, Math.Max(l.I, r.I));
            int minJ = Math.Min(a.J, Math.Min(l.J, r.J)), maxJ = Math.Max(a.J, Math.Max(l.J, r.J));
            return maxI > _g0Low && minI < _g0High && maxJ > _g0Low && minJ < _g0High;
        }

        private void Emit(V a, V l, V r)
        {
            _indices.Add(IndexOf(a));
            _indices.Add(IndexOf(l));
            _indices.Add(IndexOf(r));
            if (((a.I | a.J | l.I | l.J | r.I | r.J) & _subMask) != 0)
                DetailTriangles++;
            long ci = (long)a.I + l.I + r.I, cj = (long)a.J + l.J + r.J; // three times the centroid
            if (ci < 3L * _g0Low || ci > 3L * _g0High || cj < 3L * _g0Low || cj > 3L * _g0High)
                FarTriangles++;
        }

        /// <summary>The vertices that sit at the midpoint of a kept triangle's edge (see <see cref="ViewMesher.TJunctions"/>).</summary>
        public int CountTJunctions()
        {
            int count = 0;
            for (int t = 0; t < _indices.Count; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    (int i0, int j0, _) = _vertices[(int)_indices[t + e]];
                    (int i1, int j1, _) = _vertices[(int)_indices[t + (e + 1) % 3]];
                    if ((((i0 + i1) | (j0 + j1)) & 1) == 0 && Find((i0 + i1) / 2, (j0 + j1) / 2))
                        count++;
                }
            }
            return count;
        }

        private bool Find(int i, int j)
        {
            for (int s = 0; s < _storeVertex.Length; s++)
            {
                int k = _h.StoreIndex(s, i, j);
                if (k >= 0)
                    return _storeVertex[s][k] >= 0;
            }
            return _otherVertex.Contains(((long)i << 32) | (uint)j);
        }

        /// <summary>The vertex's index, given the first time it is met: dense maps over the stores, a hash table for the rest.</summary>
        private uint IndexOf(V v)
        {
            for (int s = 0; s < _storeVertex.Length; s++)
            {
                int k = _h.StoreIndex(s, v.I, v.J);
                if (k < 0)
                    continue;
                int index = _storeVertex[s][k];
                if (index < 0)
                {
                    index = _vertices.Count;
                    _storeVertex[s][k] = index;
                    _vertices.Add((v.I, v.J, v.H));
                }
                return (uint)index;
            }
            long key = ((long)v.I << 32) | (uint)v.J;
            int other = _otherVertex.GetOrAdd(key, _vertices.Count);
            if (other == _vertices.Count)
                _vertices.Add((v.I, v.J, v.H));
            return (uint)other;
        }
    }

    /// <summary>An open-addressing hash table from a vertex's packed coordinates (never negative) to its index.</summary>
    private sealed class VertexTable
    {
        private long[] _keys = NewKeys(1 << 16);
        private int[] _values = new int[1 << 16];
        private int _count;

        /// <summary>Whether <paramref name="key"/> is stored.</summary>
        public bool Contains(long key)
        {
            int mask = _keys.Length - 1;
            int slot = (int)StableHash.Mix((ulong)key) & mask;
            while (_keys[slot] >= 0)
            {
                if (_keys[slot] == key)
                    return true;
                slot = (slot + 1) & mask;
            }
            return false;
        }

        /// <summary>The index stored under <paramref name="key"/>, or <paramref name="value"/> after storing it there.</summary>
        public int GetOrAdd(long key, int value)
        {
            if (2 * (_count + 1) > _keys.Length)
                Grow();
            int mask = _keys.Length - 1;
            int slot = (int)StableHash.Mix((ulong)key) & mask;
            while (true)
            {
                long k = _keys[slot];
                if (k == key)
                    return _values[slot];
                if (k < 0)
                {
                    _keys[slot] = key;
                    _values[slot] = value;
                    _count++;
                    return value;
                }
                slot = (slot + 1) & mask;
            }
        }

        private void Grow()
        {
            long[] keys = _keys;
            int[] values = _values;
            _keys = NewKeys(keys.Length * 2);
            _values = new int[keys.Length * 2];
            int mask = _keys.Length - 1;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] < 0)
                    continue;
                int slot = (int)StableHash.Mix((ulong)keys[i]) & mask;
                while (_keys[slot] >= 0)
                    slot = (slot + 1) & mask;
                _keys[slot] = keys[i];
                _values[slot] = values[i];
            }
        }

        private static long[] NewKeys(int length)
        {
            var keys = new long[length];
            Array.Fill(keys, -1L);
            return keys;
        }
    }
}
