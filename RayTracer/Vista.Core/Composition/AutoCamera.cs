using Vista.Astronomy;
using Vista.Recipes;
using Vista.Scene;
using Vista.Weather;
using Vista.World;

namespace Vista.Composition;

/// <summary>How the auto camera searches (vista-plan §10 M1.14), recorded in the sidecar.</summary>
public sealed record AutoCameraSettings
{
    /// <summary>The width of each candidate's composition buffer, pixels (its height follows the frame's aspect).</summary>
    public int BufferWidth { get; init; } = 128;

    /// <summary>How many of the best candidates are rendered for the squint test, the values and the 4K mesh's triangles; 0 for none.</summary>
    public int Finalists { get; init; } = 10;

    /// <summary>At most this many finalists stand at one place (so the finalists differ).</summary>
    public int FinalistsPerPlace { get; init; } = 2;

    /// <summary>The width of a finalist's render, pixels (its height follows the frame's aspect).</summary>
    public int FinalistWidth { get; init; } = 192;

    /// <summary>A finalist's render's samples per pixel: enough for the squint test's blurred thumbnail.</summary>
    public int FinalistSamples { get; init; } = 16;

    /// <summary>The focal lengths a peak is framed with, mm (a long lens stacks the layers and makes the peak loom).</summary>
    public IReadOnlyList<double> PeakFocalLengths { get; init; } = [35.0, 50.0, 70.0, 100.0, 135.0];

    /// <summary>The focal lengths a range is framed with, mm.</summary>
    public IReadOnlyList<double> RangeFocalLengths { get; init; } = [24.0, 28.0, 35.0, 50.0, 70.0];

    /// <summary>The focal lengths tried at each place: those whose frame suits the subject's size there best.</summary>
    public int FocalLengthsPerPlace { get; init; } = 2;

    /// <summary>
    /// How high above the ground the camera stands at each place, m: a person's eye, a vantage over the near slope, a drone's height.
    /// </summary>
    public IReadOnlyList<double> HeightsAboveGround { get; init; } = [DefaultCamera.EyeHeightMetres, 40.0, 150.0];

    /// <summary>Where the summit is put, as a share of the frame's height from the top.</summary>
    public IReadOnlyList<double> SummitRows { get; init; } = [0.18, 0.3];

    /// <summary>The places around the planted viewpoint: rings at shares of the planted distance, with so many points each.</summary>
    public IReadOnlyList<PlaceRing> Rings { get; init; } = [new(0.0, 1), new(0.15, 6), new(0.3, 10), new(0.45, 14), new(0.6, 18)];

    /// <summary>The most places on shoulders just above a valley's pool (vista-plan §10 M1.13a's views), when the pools stand.</summary>
    public int Shoulders { get; init; } = 6;

    /// <summary>The defaults.</summary>
    public static AutoCameraSettings Default { get; } = new();
}

/// <summary>A ring of places around the planted viewpoint.</summary>
/// <param name="Radius">Its radius as a share of the planted distance (0: the viewpoint itself).</param>
/// <param name="Points">How many places stand on it, evenly round.</param>
public readonly record struct PlaceRing(double Radius, int Points);

/// <summary>One framing the auto camera tries: where it stands, why there, and its camera.</summary>
/// <param name="Index">Its place in the search's order (the tie-break).</param>
/// <param name="Place">Which place it stands at (an index into the search's places).</param>
/// <param name="Origin">How the place was found: <c>planted</c>, <c>ring</c>, <c>axis</c> or <c>shoulder</c>.</param>
/// <param name="Camera">The camera.</param>
public sealed record CameraCandidate(int Index, int Place, string Origin, CameraSpec Camera)
{
    /// <summary>How high above the ground at its place the camera stands, m (before the collision lifted it, if it did).</summary>
    public double AboveGroundMetres { get; init; }
}

/// <summary>A candidate with its scores.</summary>
/// <param name="Candidate">The framing.</param>
/// <param name="Scores">Its scores.</param>
/// <param name="Total">The weighted total.</param>
/// <param name="Breaks">The limit it breaks, or null.</param>
public sealed record ScoredCandidate(CameraCandidate Candidate, CompositionScores Scores, double Total, string? Breaks)
{
    /// <summary>True when it keeps every limit.</summary>
    public bool Feasible => Breaks is null;
}

/// <summary>
/// The auto camera v1 (vista-plan §4.8, §10 M1.14): good compositions for the mountain set pieces without placing a camera by hand.
/// <list type="number">
///   <item>The plant (<see cref="ShotPlanter"/>): the subject, a heading toward it with side or back light, a viewpoint across from it.</item>
///   <item>The candidates (<see cref="Candidates"/>): places around the viewpoint (rings out to three fifths of its distance, two on
///   the line to the subject nearer and farther, and shoulders just above a standing pool), each at a person's eye, 40 m and 150 m
///   up (lifted clear of the ground, <see cref="Collide"/>) where it still sees the main peak; at each, the focal lengths that suit the
///   subject's size there, the subject on the left or the right third, the summit at two heights.</item>
///   <item>The scores (<see cref="Score"/>): each candidate's composition buffer traced on the CPU (<see cref="TerrainSight"/>) and
///   scored (<see cref="CompositionScorer"/>); those that keep the limits rank above those that do not.</item>
///   <item>The finalists (<see cref="Finalists"/>): the best few, at most two at a place, are rendered (the preview session does it)
///   for the squint test, the values by layer, and their 4K mesh's triangles, and the best of them that keeps every limit is the pick.</item>
/// </list>
/// Deterministic: the same world, sun and settings give the same candidates, scores and pick.
/// </summary>
public static class AutoCamera
{
    /// <summary>What a recipe's set pieces ask the auto camera for: a peak for snow-capped mountains, a range for an alpine range alone; none without mountains.</summary>
    public static CompositionIntent IntentOf(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (recipe.SetPieces.Contains(SetPiece.SnowCappedMountains))
            return CompositionIntent.Peak;
        return recipe.SetPieces.Contains(SetPiece.AlpineRange) ? CompositionIntent.Range : CompositionIntent.None;
    }

    /// <summary>
    /// <paramref name="camera"/> lifted clear of the ground (the auto camera's collision): its eye at least
    /// <see cref="DefaultCamera.EyeHeightMetres"/> above the highest ground within 5 m of it.
    /// </summary>
    public static CameraSpec Collide(CameraSpec camera, TerrainSight sight)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(sight);
        double highest = double.NegativeInfinity;
        for (int k = 0; k < 9; k++)
        {
            (double dx, double dz) = k == 0 ? (0.0, 0.0) : Math.SinCos(k * Math.PI / 4.0);
            double ground = sight.GroundAt(camera.Position.X + 5.0 * dx, camera.Position.Z + 5.0 * dz);
            if (!double.IsNaN(ground))
                highest = Math.Max(highest, ground);
        }
        double lowest = highest + DefaultCamera.EyeHeightMetres;
        return camera.Position.Y >= lowest ? camera : camera with { Position = camera.Position with { Y = lowest } };
    }

    /// <summary>
    /// A camera at <paramref name="eye"/> framing <paramref name="summit"/> with a <paramref name="focalMm"/> lens: turned so that
    /// <paramref name="aim"/> (the subject) stands at <paramref name="across"/> of the frame's half-width from its centre (−1/3: on the
    /// left third, +1/3 on the right, 0 in the middle), and pitched so that the summit stands <paramref name="summitRow"/> of the frame's
    /// height from its top, at most 30° up or down.
    /// </summary>
    public static CameraSpec Frame(TerrainSight sight, WorldPoint eye, WorldPoint summit, WorldPoint aim, double focalMm, double across, double summitRow,
        double aspect)
    {
        ArgumentNullException.ThrowIfNull(sight);
        var camera = new CameraSpec { Position = eye, FocalLengthMm = focalMm };
        double tanV = Math.Tan(0.5 * camera.VerticalFovRadians()), tanH = tanV * aspect;
        (Vector3D toAim, _) = sight.Toward(eye, aim);
        (Vector3D toSummit, _) = sight.Toward(eye, summit);
        double azimuth = Math.Atan2(toAim.X, toAim.Z) * (180.0 / Math.PI);
        double yaw = azimuth - Math.Atan(across * tanH) * (180.0 / Math.PI);
        double elevation = Math.Asin(Math.Clamp(toSummit.Y, -1.0, 1.0)) * (180.0 / Math.PI);
        double pitch = elevation - Math.Atan((1.0 - 2.0 * summitRow) * tanV) * (180.0 / Math.PI);
        return camera with { YawDegrees = ShotPlanter.Wrap(yaw), PitchDegrees = Math.Clamp(pitch, -30.0, 30.0) };
    }

    /// <summary>
    /// The shoulders just above the valleys' pools within <paramref name="radius"/> of <paramref name="near"/> (vista-plan §10 M1.13a: a
    /// viewpoint just above the lid, looking across a valley at a range): places whose ground stands 10–120 m above the top of a pool at
    /// least 30 m deep on their own valley's floor, nearest first, at least 800 m apart; none without pools.
    /// </summary>
    public static IReadOnlyList<WorldPoint> Shoulders(ValleyPools pools, double baseMetres, TerrainSight sight, WorldPoint near, double radius, int count)
    {
        ArgumentNullException.ThrowIfNull(pools);
        ArgumentNullException.ThrowIfNull(sight);
        if (!pools.Any || count <= 0)
            return [];
        PoolLevel level = pools.Levels[0];
        PoolCell[] cells = pools.Cells[0];
        var found = new List<(double Distance, WorldPoint Point)>();
        for (int k = 0; k < cells.Length; k++)
        {
            PoolCell c = cells[k];
            if (!(c.DepthMetres >= 30.0))
                continue;
            double x = level.OriginX + (k % level.Size) * level.CellMetres, z = level.OriginZ + (k / level.Size) * level.CellMetres;
            double distance = Math.Sqrt((x - near.X) * (x - near.X) + (z - near.Z) * (z - near.Z));
            if (distance > radius)
                continue;
            double ground = sight.GroundAt(x, z);
            if (double.IsNaN(ground))
                continue;
            double above = ground + baseMetres - c.TopMetres;
            if (above is >= 10.0 and <= 120.0)
                found.Add((distance, new WorldPoint(x, ground, z)));
        }
        var chosen = new List<WorldPoint>();
        foreach ((_, WorldPoint p) in found.OrderBy(f => f.Distance).ThenBy(f => f.Point.X).ThenBy(f => f.Point.Z))
        {
            if (chosen.Count >= count)
                break;
            if (chosen.All(q => (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) >= 800.0 * 800.0))
                chosen.Add(p);
        }
        return chosen;
    }

    /// <summary>
    /// The candidates around <paramref name="shot"/> for a frame of <paramref name="aspect"/> (width over height), with
    /// <paramref name="shoulders"/> as extra places (<see cref="Shoulders"/>).
    /// </summary>
    public static IReadOnlyList<CameraCandidate> Candidates(TerrainSight sight, PlantedShot shot, double aspect, AutoCameraSettings settings,
        IReadOnlyList<WorldPoint> shoulders)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(shoulders);
        HeightField world = sight.World;
        WorldPoint subject = shot.Subject;
        (double sh, double ch) = Math.SinCos(shot.HeadingDegrees * (Math.PI / 180.0));
        var places = new List<(string Origin, double X, double Z, IReadOnlyList<double> Heights)>();
        foreach ((double radius, int points) in settings.Rings)
        {
            for (int k = 0; k < points; k++)
            {
                double angle = (k + (points % 2 == 0 ? 0.5 : 0.0)) * 2.0 * Math.PI / points;
                (double sa, double ca) = Math.SinCos(angle);
                double r = radius * shot.DistanceMetres;
                places.Add((radius == 0.0 ? "planted" : "ring", shot.Viewpoint.X + r * sa, shot.Viewpoint.Z + r * ca, settings.HeightsAboveGround));
            }
        }
        foreach (double along in (double[])[0.65, 1.4])
            places.Add(("axis", subject.X - sh * along * shot.DistanceMetres, subject.Z - ch * along * shot.DistanceMetres, settings.HeightsAboveGround));
        foreach (WorldPoint p in shoulders)
            places.Add(("shoulder", p.X, p.Z, [DefaultCamera.EyeHeightMetres, 25.0]));

        IReadOnlyList<double> focals = shot.Intent == CompositionIntent.Range ? settings.RangeFocalLengths : settings.PeakFocalLengths;
        var candidates = new List<CameraCandidate>();
        int place = 0;
        foreach ((string origin, double x, double z, IReadOnlyList<double> heights) in places)
        {
            if (x < world.OriginX + 300.0 || z < world.OriginZ + 300.0 || x > world.OriginX + world.Extent - 300.0 || z > world.OriginZ + world.Extent - 300.0)
                continue;
            double ground = sight.GroundAt(x, z);
            foreach (double above in heights)
            {
                var spec = new CameraSpec { Position = new WorldPoint(x, ground + above, z) };
                WorldPoint eye = Collide(spec, sight).Position;
                double distance = Math.Sqrt((subject.X - x) * (subject.X - x) + (subject.Z - z) * (subject.Z - z));
                if (distance < 1500.0 || !sight.Sees(eye, shot.MainPeak, 100.0 + 0.01 * distance))
                    continue;
                foreach (double focal in FocalLengthsFor(sight, eye, shot, focals, settings.FocalLengthsPerPlace, aspect))
                {
                    foreach (double across in (double[])[-1.0 / 3.0, 1.0 / 3.0])
                    {
                        foreach (double row in settings.SummitRows)
                        {
                            CameraSpec camera = Frame(sight, eye, shot.MainPeak, subject, focal, across, row, aspect);
                            candidates.Add(new CameraCandidate(candidates.Count, place, origin, camera) { AboveGroundMetres = above });
                        }
                    }
                }
                place++;
            }
        }
        return candidates;
    }

    /// <summary>
    /// The focal lengths that suit the subject from <paramref name="eye"/>, best first, at most <paramref name="count"/>: for a peak, its
    /// relief (the summit over the valley floor below it) filling about half the frame's height; for a range, its length filling about
    /// three quarters of the frame's width.
    /// </summary>
    private static IEnumerable<double> FocalLengthsFor(TerrainSight sight, WorldPoint eye, PlantedShot shot, IReadOnlyList<double> focals, int count,
        double aspect)
    {
        double Angle(Vector3D a, Vector3D b) => Math.Acos(Math.Clamp(Vector3D.Dot(a, b), -1.0, 1.0));
        double target, size;
        if (shot.Intent == CompositionIntent.Range)
        {
            (double sa, double ca) = Math.SinCos(shot.RangeAxisDegrees * (Math.PI / 180.0));
            WorldPoint m = shot.Massif;
            double half = shot.RangeHalfLengthMetres;
            (Vector3D a, _) = sight.Toward(eye, new WorldPoint(m.X + sa * half, m.Y, m.Z + ca * half));
            (Vector3D b, _) = sight.Toward(eye, new WorldPoint(m.X - sa * half, m.Y, m.Z - ca * half));
            size = Angle(a, b);
            target = 0.75;
        }
        else
        {
            WorldPoint p = shot.MainPeak;
            (Vector3D top, _) = sight.Toward(eye, p);
            (Vector3D foot, _) = sight.Toward(eye, p with { Y = shot.FloorMetres });
            size = Angle(top, foot);
            target = 0.5;
        }
        double Fill(double focal)
        {
            double fov = 2.0 * Math.Atan(12.0 / focal);
            if (shot.Intent == CompositionIntent.Range)
                fov = 2.0 * Math.Atan(Math.Tan(0.5 * fov) * aspect);
            return size / fov;
        }
        return focals.OrderBy(f => Math.Abs(Math.Log(Fill(f) / target))).ThenBy(f => f).Take(count);
    }

    /// <summary>
    /// Scores every candidate (stage one: its composition buffer, <see cref="AutoCameraSettings.BufferWidth"/> wide, traced on the CPU);
    /// the candidates run in parallel, each buffer on one thread, so the scores are the same however the work is shared out.
    /// </summary>
    public static ScoredCandidate[] Score(TerrainSight sight, IReadOnlyList<CameraCandidate> candidates, SunPosition sun, PlantedShot shot,
        double aspect, AutoCameraSettings settings, CompositionWeights weights, CompositionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentNullException.ThrowIfNull(settings);
        int width = settings.BufferWidth, height = Math.Max(4, (int)Math.Round(width / aspect));
        var scored = new ScoredCandidate[candidates.Count];
        Parallel.For(0, candidates.Count, k =>
        {
            CameraCandidate candidate = candidates[k];
            CompositionBuffer buffer = sight.Trace(candidate.Camera, width, height);
            scored[k] = Scored(candidate, CompositionScorer.Score(buffer, Context(candidate.Camera, sun, shot), limits, sight), weights, limits);
        });
        return scored;
    }

    /// <summary>The context a candidate of <paramref name="shot"/> is scored in.</summary>
    public static FramingContext Context(CameraSpec camera, SunPosition sun, PlantedShot shot)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(shot);
        WorldPoint s = shot.Subject;
        double distance = Math.Sqrt((s.X - camera.Position.X) * (s.X - camera.Position.X) + (s.Z - camera.Position.Z) * (s.Z - camera.Position.Z));
        return new FramingContext(camera, sun, shot.Intent, shot.MainPeak, distance);
    }

    /// <summary><paramref name="candidate"/> with <paramref name="scores"/>, its total and the limit it breaks.</summary>
    public static ScoredCandidate Scored(CameraCandidate candidate, CompositionScores scores, CompositionWeights weights, CompositionLimits limits) =>
        new(candidate, scores, scores.Total(weights), scores.Breaks(limits));

    /// <summary>The candidates best first: those that keep the limits before those that do not, then by total, then by their order.</summary>
    public static ScoredCandidate[] Rank(IEnumerable<ScoredCandidate> scored)
    {
        ArgumentNullException.ThrowIfNull(scored);
        return [.. scored.OrderBy(s => s.Feasible ? 0 : 1).ThenByDescending(s => s.Total).ThenBy(s => s.Candidate.Index)];
    }

    /// <summary>The finalists from <paramref name="ranked"/>: the best that keep the limits, at most <see cref="AutoCameraSettings.FinalistsPerPlace"/> at a place.</summary>
    public static ScoredCandidate[] Finalists(IReadOnlyList<ScoredCandidate> ranked, AutoCameraSettings settings)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        ArgumentNullException.ThrowIfNull(settings);
        var perPlace = new Dictionary<int, int>();
        var finalists = new List<ScoredCandidate>();
        foreach (ScoredCandidate s in ranked)
        {
            if (finalists.Count >= settings.Finalists || !s.Feasible)
                break;
            int at = perPlace.GetValueOrDefault(s.Candidate.Place);
            if (at >= settings.FinalistsPerPlace)
                continue;
            perPlace[s.Candidate.Place] = at + 1;
            finalists.Add(s);
        }
        return [.. finalists];
    }
}
