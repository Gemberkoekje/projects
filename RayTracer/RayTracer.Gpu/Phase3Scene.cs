using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

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
        IReadOnlyList<Light>? extraLights = null,
        float wallThickness = 0f)
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(mazeSize, mazeSize, seed: mazeSeed);

        materials.TryGetMaterial("01138", out MaterialData goal);
        materials.TryGetMaterial("01085", out MaterialData mortar);
        materials.TryGetMaterial("01138", out MaterialData grid);

        // Resolve wall-feature conflicts with a precedence (decal > mirror > window):
        //   • a mirror only lands on a wall that has no sign;
        //   • a window only replaces a wall that has neither a sign nor a mirror.
        // All three are keyed on the same (gx, gy, horizontal) wall id, so the exclusions
        // are just set membership. Everything stays null-safe for the fixed-seed scenes.
        HashSet<(int, int, bool)> decalWalls = props is not null ? MazeProps.SignWalls(maze, props) : [];
        HashSet<(int, int, bool)> mirrorWalls = mirrors is not null
            ? MazeMirrors.SelectWalls(maze, mirrors, decalWalls)
            : [];
        var windowBlocked = new HashSet<(int, int, bool)>(decalWalls);
        windowBlocked.UnionWith(mirrorWalls);

        // Windows replace a subset of interior walls: those brick walls are omitted here
        // so the window (built below) shows through to the adjacent cell.
        Func<int, int, bool, bool>? skipWall = windows is not null
            ? MazeWindows.SkipPredicate(maze, windows, windowBlocked)
            : null;

        Tracable[] tracables = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01138"],
            ceilingMaterial: materials["01085"],
            mortarMaterial: mortar,
            ceilingGridMaterial: grid,
            goalMaterial: goal,
            skipWall: skipWall,
            wallThickness: wallThickness);

        // Classic prop decals (rat/logo/signs — plan §3–§5), appended before packing so
        // they become GPU quads. Off by default (fixed-seed self-test scenes stay
        // decal-free, preserving their CPU parity); the productized path opts in.
        if (props is not null)
        {
            List<Tracable> decals = MazeProps.Build(maze, materials["00115"], props);
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
            seed: lightSeed);

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
    /// near the maze centre, low to the floor. It is now the maze's only traced moving object
    /// alongside the bubbles, so it moves at a gentle amble rather than a fast scurry — slower
    /// per-frame motion lets the temporal accumulator hold more consistent samples per pixel, so
    /// the hero-sampled sprite shows far fewer fireflies while still forcing motion mode.
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
            MoveTime = 1.6f,
            TurnTime = 1.2f,
            StillTime = 1.0f,
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
}
