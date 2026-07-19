using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// The open-air half of the maze (outdoor-area-plan.md §O0): the far <see cref="Fraction"/> of the maze
/// (the rows furthest from the (0,0) start) becomes roofless (§O1) and is lit by a directional sun
/// (§C2) + a procedural sky (§O2). <see cref="SunDirection"/> is the sunlight's travel direction (the
/// sun disk is toward its negation); pass the same vector to <c>Phase6Renderer.SetSky</c>.
/// </summary>
public sealed record OutdoorOptions(
    float Fraction = 0.5f,
    Vector3 SunDirection = default,
    float SunAngularRadius = 0.04f,
    float SunTempK = 5500f) // §O9/§O8: sun emission colour temperature (the day/night cycle drives it)
{
    /// <summary>The travel direction, defaulting to a slanted afternoon sun when left unset.</summary>
    public Vector3 EffectiveSunDirection =>
        SunDirection.LengthSquared() > 1e-12f ? Vector3.Normalize(SunDirection) : Vector3.Normalize(new Vector3(0.4f, -1f, 0.35f));
}

/// <summary>
/// Builds the maze scene, lights, camera, and GPU resources for Phase 3. Same
/// scene as <see cref="Phase2Scene"/> (fixed seeds so the render is deterministic
/// and cross-checkable against the CPU reference) but additionally exposes the
/// <see cref="Maze"/> and a <see cref="CreateCameraController"/> factory so the
/// windowed temporal demo can walk the maze autonomously — motion is what
/// exercises the TAA reprojection and the accumulation reset lifecycle.
/// </summary>
internal sealed class Phase3Scene
{
    public required Maze Maze { get; init; }
    public required Tracable[] Tracables { get; init; }
    public required PackedScene Packed { get; init; }
    public required SpectralResources Spectral { get; init; }
    public required Light[] Lights { get; init; }
    public required PackedLights PackedLights { get; init; }
    public required Camera Camera { get; init; }
    public required WavelengthLookup Wavelengths { get; init; }
    public required float CellSize { get; init; }
    public required float EyeHeight { get; init; }

    /// <summary>§O2: the sun's travel direction when the maze has an outdoor half; null otherwise. The
    /// caller passes it to <c>Phase6Renderer.SetSky</c> so the visible sky and shadow-casting sun agree.</summary>
    public Vector3? SunDirection { get; init; }

    /// <summary>Angular radius of the sun (radians) for soft sun shadows + the sky's sun disk.</summary>
    public float SunAngularRadius { get; init; }

    /// <summary>Index of the directional sun in <see cref="Lights"/> (it is appended last), or -1 when there
    /// is no outdoor sun. Lets the day/night cycle (§O8) update just the sun light per frame.</summary>
    public int SunLightIndex { get; init; }

    /// <summary>Streaming-bubble flight descriptors (spectral-effects-plan §2.6), in the same order as
    /// <see cref="PackedScene.Spheres"/> — bubbles are the only spheres in the maze. Empty when
    /// bubbles are off. The windowed loop animates these each frame via <c>Phase6Renderer.UpdateSpheres</c>.</summary>
    public required IReadOnlyList<MazeBubbles.Bubble> Bubbles { get; init; }

    public static Phase3Scene Build(int width, int height, int mazeSeed = 12345, int mazeSize = 16, int lightSeed = 777,
        MazeProps.Options? props = null,
        MazeMirrors.Options? mirrors = null,
        MazeWindows.Options? windows = null,
        MazeJewels.Options? jewels = null,
        MazeOilSlicks.Options? oilSlicks = null,
        MazeBubbles.Options? bubbles = null,
        MazeWater.Options? water = null,
        IReadOnlyList<Light>? extraLights = null,
        float wallThickness = 0f,
        float torchTempK = 0f,
        OutdoorOptions? outdoor = null)
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(mazeSize, mazeSize, seed: mazeSeed);

        // §O0: mark the far half outdoor before building geometry, so MazeGeometryBuilder opens its roof
        // (§O1). Must precede Build. Left off (null) → an all-indoor roofed maze, bit-identical.
        if (outdoor is not null)
            maze.MarkOutdoorRegion(outdoor.Fraction);

        materials.TryGetMaterial("01138", out MaterialData goal);
        materials.TryGetMaterial("01085", out MaterialData mortar);
        materials.TryGetMaterial("01138", out MaterialData grid);

        // Resolve wall-feature conflicts with a precedence (decal > mirror > window):
        //   • a mirror only lands on a wall that has no sign;
        //   • a window only replaces a wall that has neither a sign nor a mirror.
        // All three are keyed on the same (gx, gy, horizontal) wall id, so the exclusions
        // are just set membership. Everything stays null-safe for the fixed-seed scenes.
        // §O: indoor decor (signs, mirrors, windows) stays out of the open garden — the hedge walls
        // (purely-Outdoor) are excluded from all three. Indoor↔Outdoor boundary walls are full stone (not
        // hedges), so they still get signs/mirrors/windows ("features between the inside and the outside").
        HashSet<(int, int, bool)> outdoorWalls = outdoor is not null ? MazeHedges.HedgeWalls(maze) : [];
        HashSet<(int, int, bool)> decalWalls = props is not null ? MazeProps.SignWalls(maze, props, outdoorWalls) : [];
        var mirrorBlocked = new HashSet<(int, int, bool)>(decalWalls);
        mirrorBlocked.UnionWith(outdoorWalls);
        HashSet<(int, int, bool)> mirrorWalls = mirrors is not null
            ? MazeMirrors.SelectWalls(maze, mirrors, mirrorBlocked)
            : [];
        var windowBlocked = new HashSet<(int, int, bool)>(decalWalls);
        windowBlocked.UnionWith(mirrorWalls);
        windowBlocked.UnionWith(outdoorWalls);

        // Windows replace a subset of interior walls: those brick walls are omitted here
        // so the window (built below) shows through to the adjacent cell.
        Func<int, int, bool, bool>? windowSkip = windows is not null
            ? MazeWindows.SkipPredicate(maze, windows, windowBlocked)
            : null;
        // §O4: outdoor walls become low hedges — skip the tall stone wall where a hedge replaces it.
        Func<int, int, bool, bool>? hedgeSkip = outdoor is not null ? MazeHedges.SkipPredicate(maze) : null;
        Func<int, int, bool, bool>? skipWall = (windowSkip, hedgeSkip) switch
        {
            (null, null) => null,
            (not null, null) => windowSkip,
            (null, not null) => hedgeSkip,
            _ => (gx, gy, h) => windowSkip!(gx, gy, h) || hedgeSkip!(gx, gy, h),
        };

        // §O5: grass on outdoor cell floors (bright green); hedges are a darker green (built below).
        MaterialData? grass = outdoor is not null ? Foliage("outdoor-grass", new Vector3(0.26f, 0.52f, 0.16f)) : null;

        Tracable[] tracables = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01138"],
            ceilingMaterial: materials["01085"],
            mortarMaterial: mortar,
            ceilingGridMaterial: grid,
            goalMaterial: goal,
            grassMaterial: grass,
            skipWall: skipWall,
            wallThickness: wallThickness);

        // §O4: low hedges in place of the tall stone walls that skipWall removed above.
        if (outdoor is not null)
        {
            List<Tracable> hedges = MazeHedges.Build(maze, Foliage("outdoor-hedge", new Vector3(0.10f, 0.34f, 0.13f)));
            if (hedges.Count > 0)
                tracables = [.. tracables, .. hedges];

            // §O10: frame each Indoor↔Outdoor passage with a stone doorway (jambs + lintel + sill) so the
            // boundary reads as a portal, not a torn-off wall. A warm grey cut-stone trim contrasts with the
            // terracotta brick so the frame reads clearly.
            List<Tracable> thresholds = MazeThreshold.Build(
                maze, Foliage("doorway-stone", new Vector3(0.48f, 0.46f, 0.42f)), wallThickness: wallThickness);
            if (thresholds.Count > 0)
                tracables = [.. tracables, .. thresholds];
        }

        // Classic prop decals (rat/logo/signs — plan §3–§5), appended before packing so
        // they become GPU quads. Off by default (fixed-seed self-test scenes stay
        // decal-free, preserving their CPU parity); the productized path opts in.
        if (props is not null)
        {
            List<Tracable> decals = MazeProps.Build(maze, materials["00115"], props, wallThickness: wallThickness, excludeWalls: outdoorWalls);
            if (decals.Count > 0)
                tracables = [.. tracables, .. decals];
        }

        // Framed mirrors on the selected (sign-free) walls (spectral-effects-plan §1.1).
        if (mirrors is not null && mirrorWalls.Count > 0)
        {
            List<Tracable> mirrorQuads = MazeMirrors.Build(maze, mirrorWalls, wallThickness: wallThickness);
            if (mirrorQuads.Count > 0)
                tracables = [.. tracables, .. mirrorQuads];
        }

        // Clear-glass windows replacing the walls skipped above (spectral-effects-plan §1.2).
        if (windows is not null)
        {
            List<Tracable> windowQuads = MazeWindows.Build(
                maze, windows, windowBlocked, wallMaterial: materials["00115"], mortarMaterial: mortar,
                sillMaterial: grid, wallThickness: wallThickness);
            if (windowQuads.Count > 0)
                tracables = [.. tracables, .. windowQuads];
        }

        // Floating faceted crystals — the maze's dispersive signature object
        // (spectral-effects-plan §2.1). Gated the same way: null for fixed-seed
        // self-test / golden scenes so those stay bit-exact.
        if (jewels is not null)
        {
            List<Tracable> jewelQuads = MazeJewels.Build(maze, jewels);
            if (jewelQuads.Count > 0)
                tracables = [.. tracables, .. jewelQuads];
        }

        // Iridescent oil puddles on select cell floors (thin-film interference, §2.2).
        // Gated the same way: null for fixed-seed self-test / golden scenes.
        if (oilSlicks is not null)
        {
            List<Tracable> oilQuads = MazeOilSlicks.Build(maze, oilSlicks);
            if (oilQuads.Count > 0)
                tracables = [.. tracables, .. oilQuads];
        }

        // §O6: ornamental water pools in select Outdoor cells (dielectric surface over a tinted basin).
        // Gated null for fixed-seed self-test / golden scenes; Cells() also filters to Outdoor cells only.
        if (water is not null)
        {
            List<Tracable> waterQuads = MazeWater.Build(maze, water);
            if (waterQuads.Count > 0)
                tracables = [.. tracables, .. waterQuads];
        }

        // Streaming soap bubbles — thin-film-on-a-sphere (§2.6), the only spheres (and the only moving
        // geometry) in the maze. Gated null for fixed-seed self-test / golden scenes so those stay
        // bit-exact (no spheres ⇒ triangle-only BLAS). The flight list is returned so the windowed
        // loop can animate the streams each frame; the built spheres go in at their time-0 positions.
        // A bubble emitter never shares a cell with another feature — collect the occupied cells
        // (jewels + oil slicks by cell, mirrors + signs by their wall's cell) and exclude them.
        var occupiedCells = new HashSet<(int, int)>();
        if (jewels is not null)
            foreach ((Vector3 c, float _) in MazeJewels.Placements(maze, jewels))
                occupiedCells.Add(((int)MathF.Floor(c.X / MazeGeometryBuilder.CellSize),
                                   (int)MathF.Floor(c.Z / MazeGeometryBuilder.CellSize)));
        if (oilSlicks is not null)
            foreach ((int gx, int gy, int _, float _) in MazeOilSlicks.Cells(maze, oilSlicks))
                occupiedCells.Add((gx, gy));
        if (water is not null)
            foreach ((int gx, int gy) in MazeWater.Cells(maze, water))
                occupiedCells.Add((gx, gy));
        foreach ((int gx, int gy, bool _) in decalWalls) occupiedCells.Add((gx, gy));
        foreach ((int gx, int gy, bool _) in mirrorWalls) occupiedCells.Add((gx, gy));

        IReadOnlyList<MazeBubbles.Bubble> bubbleList = bubbles is not null
            ? MazeBubbles.Bubbles(maze, bubbles, occupiedCells)
            : [];
        if (bubbles is not null && bubbleList.Count > 0)
        {
            List<Tracable> bubbleSpheres = MazeBubbles.Build(maze, bubbles, occupiedCells);
            tracables = [.. tracables, .. bubbleSpheres];
        }

        Light[] lights = MazeGeometryBuilder.BuildLights(
            maze,
            lightSpawnChance: 0.4f,
            biomeSize: 4,
            seed: lightSeed,
            emissionTempK: torchTempK); // §O9: warm torches (0 = flat white → goldens bit-exact)

        // §C2/§O2: add the directional sun that lights the open half (and drives the sky's disk).
        // §O9: blackbody emission (default 5500 K daylight; §O8 varies it). It is appended last, so its
        // index (SunLightIndex) lets the windowed loop update just the sun as the day/night cycle advances.
        int sunLightIndex = -1;
        if (outdoor is not null)
        {
            sunLightIndex = lights.Length;
            lights = [.. lights, new Light { Directional = true, Direction = outdoor.EffectiveSunDirection, Color = Vector3.One, Radius = outdoor.SunAngularRadius, EmissionTempK = outdoor.SunTempK }];
        }

        if (extraLights is { Count: > 0 })
            lights = [.. lights, .. extraLights];

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        float eyeHeight = wh * 0.5f;

        var camera = new Camera
        {
            Position = new Vector3(cs * 0.5f, eyeHeight, cs * 0.5f),
            Rotation = CameraController.HeadingToQuaternion(Direction.South),
            Fov = MathF.PI / 3f,
            Aspect = (float)width / height,
            ImgPlaneZ = 1f,
        };

        var wavelengths = new WavelengthLookup();
        PackedScene packed = GpuScenePacker.Pack(tracables);
        SpectralResources spectral = SpectralResourceBaker.Bake(wavelengths, packed.Materials);
        PackedLights packedLights = LightPacker.Pack(lights);

        return new Phase3Scene
        {
            Maze = maze,
            Tracables = tracables,
            Packed = packed,
            Spectral = spectral,
            Lights = lights,
            PackedLights = packedLights,
            Camera = camera,
            Wavelengths = wavelengths,
            CellSize = cs,
            EyeHeight = eyeHeight,
            Bubbles = bubbleList,
            SunDirection = outdoor?.EffectiveSunDirection,
            SunAngularRadius = outdoor?.SunAngularRadius ?? 0f,
            SunLightIndex = sunLightIndex,
        };
    }

    /// <summary>Creates an autonomous right-hand-rule walker starting at the
    /// camera's initial cell, heading south.</summary>
    public CameraController CreateCameraController()
    {
        var navigator = new MazeNavigator(Maze, startX: 0, startY: 0, startHeading: Direction.South);
        return new CameraController(navigator, CellSize, EyeHeight);
    }

    /// <summary>
    /// Creates the animated rat's walker (plan §8): its own right-hand-rule navigator starting
    /// near the maze centre, low to the floor. The rat and bubbles no longer get a per-pixel
    /// accumulation reset — they converge with the same sample budget as the rest of the frame — so
    /// the rat is now walked at a slow amble (roughly half the previous speed) to keep the temporal
    /// smear of a moving object short enough that it still reads as a rat rather than a streak.
    /// The <paramref name="ratCam"/> is a throw-away camera whose <c>Position</c> is the rat's
    /// world position each frame.
    /// </summary>
    public CameraController CreateRatController(out Camera ratCam)
    {
        const float ratHeight = 0.25f;
        int sx = Maze.Width / 2, sy = Maze.Height / 2;
        var navigator = new MazeNavigator(Maze, sx, sy, Direction.East);
        var ctrl = new CameraController(navigator, CellSize, ratHeight)
        {
            MoveTime = 3.2f,
            TurnTime = 2.2f,
            StillTime = 2.0f,
        };
        Camera sceneCam = Camera;
        ratCam = new Camera
        {
            Position = new Vector3((sx + 0.5f) * CellSize, ratHeight, (sy + 0.5f) * CellSize),
            Rotation = Quaternion.Identity,
            Fov = sceneCam.Fov,
            Aspect = sceneCam.Aspect,
            ImgPlaneZ = sceneCam.ImgPlaneZ,
        };
        return ctrl;
    }

    /// <summary>
    /// Builds a diffuse spectral material from an RGB tint using broad, overlapping Gaussian bands
    /// (§O4/§O5 grass + hedges). Mirrors the Program-level <c>ColoredDiffuse</c>: the bands overlap so
    /// the colour reads without harsh spectral edges while staying a genuine per-wavelength reflectance.
    /// </summary>
    private static MaterialData Foliage(string id, Vector3 rgb)
    {
        const int min = 360, max = 830, step = 5;
        int count = (max - min) / step + 1;
        var wavelengths = new int[count];
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            int w = min + i * step;
            wavelengths[i] = w;
            float r = MathF.Exp(-MathF.Pow((w - 620f) / 70f, 2f));
            float g = MathF.Exp(-MathF.Pow((w - 540f) / 60f, 2f));
            float b = MathF.Exp(-MathF.Pow((w - 460f) / 55f, 2f));
            values[i] = Math.Clamp(rgb.X * r + rgb.Y * g + rgb.Z * b, 0f, 1f);
        }
        return new MaterialData(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            new SpectralData(wavelengths, values, (float[])values.Clone()), SurfaceKind.Diffuse);
    }
}
