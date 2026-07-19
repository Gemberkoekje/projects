using System.Numerics;
using RayTracer;
using RayTracer.Gpu;
using Pinball.Content;
using Pinball.Physics;
using Pinball.Game;

namespace Pinball.App;

/// <summary>
/// Space Cadet RT — the pinball game host (pinball-plan §7.3 / §8 P6). A standalone WinForms executable that
/// runs the deterministic <c>Pinball.Core</c> physics on the reusable <c>RayTracer.Gpu</c> DXR backend — the
/// same seam the maze app sits on. No maze code: this is the proof the Milestone E engine/app split is real.
/// <para><b>Run:</b> <c>dotnet run --project Pinball.App</c> opens the interactive game (Z / · Space launch ·
/// ←→↑ nudge · Esc). Headless: <c>--pinball-sim</c>, <c>--pinball-play</c>, <c>--table-demo</c>,
/// <c>--ball-sweep</c>, <c>--flipper-demo</c>, <c>--table-regress [--update]</c>.</para>
/// </summary>
internal static class Program
{
    private const int Width = 1280;
    private const int Height = 720;
    private const float Phase3FireflyClamp = 3.0f; // per-sample firefly clamp (matches the maze default)

    // WinForms one-time init (ApplicationConfiguration.Initialize must run once before the first window).
    private static bool _appConfigured;
    internal static void EnsureAppConfigured()
    {
        if (_appConfigured) return;
        _appConfigured = true;
        ApplicationConfiguration.Initialize();
    }

    [STAThread]
    private static int Main(string[] args)
    {
        float sampleClamp = ParseFloat(args, "--clamp", Phase3FireflyClamp);
        int frames = ParseInt(args, "--frames", 0);
        string? save = ParseString(args, "--save");
        string? tablePath = ParseString(args, "--table");        // load a table JSON (editor output)
        string? exportPath = ParseString(args, "--table-export"); // dump the built-in walls to JSON

        if (exportPath != null)
            return ExportTable(exportPath);

        if (args.Contains("--pinball-sim", StringComparer.OrdinalIgnoreCase))
            return RunPinballSim(frames, save ?? "pinball-sim.png", sampleClamp);
        if (args.Contains("--pinball-play", StringComparer.OrdinalIgnoreCase))
            return RunPinballPlay(frames, save ?? "pinball-play.png", sampleClamp);
        if (args.Contains("--table-demo", StringComparer.OrdinalIgnoreCase))
            return RunTableDemo(frames, save ?? "table-demo.png", sampleClamp);
        if (args.Contains("--table-topdown", StringComparer.OrdinalIgnoreCase))
            return RunTableTopDown(frames, save ?? "table-topdown.png", sampleClamp, tablePath);
        if (args.Contains("--ball-sweep", StringComparer.OrdinalIgnoreCase))
            return RunBallSweep(frames, save ?? "ball-sweep.png", sampleClamp,
                !args.Contains("--static-ball", StringComparer.OrdinalIgnoreCase));
        if (args.Contains("--flipper-demo", StringComparer.OrdinalIgnoreCase))
            return RunFlipperDemo(frames, save ?? "flipper-demo.png", sampleClamp);
        if (args.Contains("--table-regress", StringComparer.OrdinalIgnoreCase))
            return TableRegression.Run(args.Contains("--update", StringComparer.OrdinalIgnoreCase));

        return RunPinballWindowed(sampleClamp); // default: the interactive game
    }

    private static float ParseFloat(string[] a, string flag, float def)
    {
        int i = Array.FindIndex(a, x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < a.Length &&
            float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : def;
    }
    private static int ParseInt(string[] a, string flag, int def)
    {
        int i = Array.FindIndex(a, x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out int v) ? v : def;
    }
    private static string? ParseString(string[] a, string flag)
    {
        int i = Array.FindIndex(a, x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    // Writes a tightly-packed RGBA8 buffer to a PNG.
    internal static void SavePng(byte[] rgba, int width, int height, string path)
    {
        using var bmp = BitmapFromRgba(rgba, width, height);
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static System.Drawing.Bitmap BitmapFromRgba(byte[] rgba, int width, int height)
    {
        var bmp = new System.Drawing.Bitmap(
            width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new System.Drawing.Rectangle(0, 0, width, height);
        System.Drawing.Imaging.BitmapData data =
            bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        try
        {
            var row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int s = (y * width + x) * 4;
                    int d = x * 4;
                    row[d + 0] = rgba[s + 2]; // B
                    row[d + 1] = rgba[s + 1]; // G
                    row[d + 2] = rgba[s + 0]; // R
                    row[d + 3] = 255;         // A
                }
                System.Runtime.InteropServices.Marshal.Copy(
                    row, 0, System.IntPtr.Add(data.Scan0, y * data.Stride), width * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    // ── Scene helpers (duplicated from the maze — a separate assembly can't see its internals). ──
    private static Quaternion LookRotation(Vector3 pos, Vector3 target, Vector3 up)
    {
        Vector3 f = Vector3.Normalize(target - pos);
        Vector3 r = Vector3.Normalize(Vector3.Cross(up, f));
        Vector3 u = Vector3.Cross(f, r);
        var m = new Matrix4x4(
            r.X, r.Y, r.Z, 0f,
            u.X, u.Y, u.Z, 0f,
            f.X, f.Y, f.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.CreateFromRotationMatrix(m);
    }

    private static MaterialData ColoredDiffuse(string id, Vector3 rgb)
    {
        const int min = 360, max = 830, step = 5;
        int count = (max - min) / step + 1;
        var wavelengths = new int[count];
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            int w = min + i * step;
            wavelengths[i] = w;
            // Broad, overlapping RGB bands so the colour reads without harsh spectral edges.
            float r = MathF.Exp(-MathF.Pow((w - 620f) / 70f, 2f));
            float g = MathF.Exp(-MathF.Pow((w - 540f) / 60f, 2f));
            float b = MathF.Exp(-MathF.Pow((w - 460f) / 55f, 2f));
            values[i] = Math.Clamp(rgb.X * r + rgb.Y * g + rgb.Z * b, 0f, 1f);
        }
        return new MaterialData(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            new SpectralData(wavelengths, values, (float[])values.Clone()), SurfaceKind.Diffuse);
    }

    private static TracableRectangle WallZ(float zPlane, float x0, float x1, float y0, float y1, MaterialData mat)
        => new((new Vector3(x0, y0, zPlane), new Vector3(x1, y0, zPlane), new Vector3(x0, y1, zPlane)), mat);

    // A horizontal quad at height y spanning [x0,x1] × [z0,z1].
    private static TracableRectangle FloorQuad(float y, float x0, float x1, float z0, float z1, MaterialData mat)
        => new((new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x0, y, z1)), mat);

    private static MaterialData FlatDiffuse(string id, float reflectance)
        => new(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, FlatSpectrum(reflectance), SurfaceKind.Diffuse);

    private static SpectralData FlatSpectrum(float value)
    {
        const int min = 360, max = 830, step = 5;
        int count = (max - min) / step + 1;
        var wavelengths = new int[count];
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            wavelengths[i] = min + i * step;
            values[i] = value;
        }
        return new SpectralData(wavelengths, values, (float[])values.Clone());
    }

    // ── Pinball entry points (moved from RayTracer.Maze — §7.3). ──
    /// <summary>
    /// Space Cadet RT — P0 static-table thin slice (pinball-plan §5.3 / §8 P0). Converges the
    /// <see cref="PinballTableScene"/> (the single source-of-truth playfield) through the shipping Phase 6
    /// path and writes a PNG. Shows the new <see cref="SurfaceKind.Emissive"/> neon inserts, a chrome ball
    /// reflecting them, a dispersive glass gem, a mirror rail, and a thin-film bumper — no gameplay, no
    /// movers. The scene is fixed and deterministic, so the capture reproduces run-to-run on the same GPU.
    /// </summary>
    private static int RunTableDemo(int frames, string savePath, float sampleClamp)
    {
        if (frames <= 0) frames = 600; // neon glow + chrome/glass are hero-λ → give it samples to converge
        Console.WriteLine($"Space Cadet RT — static table (P0 thin slice, {frames} frames) -> {savePath}");

        PinballTableScene table = PinballTableScene.Build(Width, Height);
        Console.WriteLine($"  quads: {table.Packed.Primitives.Length}, spheres: {table.Packed.Spheres.Count}, lights: {table.PackedLights.Positions.Length}");

        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.None);
        using var renderer = new Phase6Renderer(
            Width, Height, table.Packed, table.Spectral, table.PackedLights, table.Camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp,
            maxSampleCount: (uint)Math.Max(1, frames),
            biomeIndicator: false, debugMode: Phase5DebugMode.Beauty, decalAtlas: Phase6Renderer.EmptyDecalAtlas);
        renderer.Initialize(windowHandle: 0);
        renderer.SetCamera(table.Camera);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        for (int f = 0; f < frames; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);

        byte[] rgba = renderer.ReadbackOutput();
        SavePng(rgba, Width, Height, savePath);
        Console.WriteLine($"Saved {Width}x{Height} PNG to {savePath}");
        return 0;
    }

    /// <summary>
    /// A DEBUG straight-down orthographic-ish view of the table for authoring wall geometry: the camera looks
    /// down −Y from high above the playfield centre with up = +Z, so the render maps linearly to table
    /// coordinates (+x right, +z up-table) — matching the real-table reference photos and making it easy to
    /// place walls. Portrait resolution to fit the tall playfield. Not a golden; a scaffold for geometry work.
    /// </summary>
    /// <summary>Writes the built-in wall set to a JSON table file (seed for the editor / round-trip check).</summary>
    private static int ExportTable(string path)
    {
        var def = Pinball.Content.TableDefinition.FromWalls(Pinball.Physics.TableWalls.All, Pinball.Physics.TableWalls.WallHeight);
        System.IO.File.WriteAllText(path, def.ToJson());
        Console.WriteLine($"Wrote {def.Walls.Count} walls -> {path}");
        return 0;
    }

    private static int RunTableTopDown(int frames, string savePath, float sampleClamp, string? tablePath = null)
    {
        if (frames <= 0) frames = 200;
        const int topW = 620, topH = 1040; // portrait, ≈ table aspect (11 × 24)
        Console.WriteLine($"Space Cadet RT — top-down geometry view ({frames} frames) -> {savePath}"
            + (tablePath != null ? $"  [table: {tablePath}]" : ""));

        Pinball.Physics.TableWalls.Wall[]? walls = null;
        Vector3? ballStart = null; float ballRadius = 0.36f;
        if (tablePath != null)
        {
            Pinball.Content.TableDefinition def = Pinball.Content.TableDefinition.Load(tablePath);
            walls = def.ToWalls();
            if (def.Ball is { Start.Length: >= 2 } b)
            {
                ballStart = new Vector3((float)b.Start[0], 0f, (float)b.Start[1]);
                ballRadius = Math.Max(0.02f, (float)b.Radius); // clamp: a 0/negative radius would invert the sphere
            }
        }
        PinballTableScene table = PinballTableScene.BuildWallsOnly(topW, topH, walls, ballStart, ballRadius); // walls only, for geometry authoring
        Vector3 camPos = new(0f, 46f, 12f); // straight above the playfield centre (z 0..24 → centre 12)
        var cam = new Camera
        {
            Position = camPos,
            Rotation = LookRotation(camPos, new Vector3(0f, 0f, 12f), new Vector3(0f, 0f, 1f)),
            Fov = MathF.PI / 6.2f, // ≈29° vertical — fits the 24-unit length with a little margin
            Aspect = (float)topW / topH,
            ImgPlaneZ = 1f,
        };

        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.None);
        using var renderer = new Phase6Renderer(
            topW, topH, table.Packed, table.Spectral, table.PackedLights, cam,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp,
            maxSampleCount: (uint)Math.Max(1, frames),
            biomeIndicator: false, debugMode: Phase5DebugMode.Beauty, decalAtlas: Phase6Renderer.EmptyDecalAtlas);
        renderer.Initialize(windowHandle: 0);
        renderer.SetCamera(cam);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        for (int f = 0; f < frames; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);

        SavePng(renderer.ReadbackOutput(), topW, topH, savePath);
        Console.WriteLine($"Saved {topW}x{topH} PNG to {savePath}");
        return 0;
    }

    /// <summary>
    /// P2 ghost-trail verification (pinball-plan §4.3): sweeps the chrome ball across the playfield over the
    /// converge frames while the camera stays fixed, then saves the final accumulated frame. With the
    /// ball tagged <c>Dynamic</c> (default), the §4.3 hit-id restart clears each pixel the ball vacates, so
    /// the ball appears cleanly at its end position with no trail. With <c>--static-ball</c> the ball is
    /// untagged: its pixels never restart on the move, so the swept path smears into a permanent ghost
    /// trail — the artifact the fix removes.
    /// </summary>
    private static int RunBallSweep(int frames, string savePath, float sampleClamp, bool dynamicBall)
    {
        if (frames <= 0) frames = 90;
        Console.WriteLine($"Space Cadet RT — ball sweep ({frames} frames, {(dynamicBall ? "Dynamic ball" : "STATIC ball → trail")}) -> {savePath}");

        PinballTableScene table = PinballTableScene.Build(Width, Height, dynamicBall);
        int n = table.Packed.Spheres.Count;
        var centers = new Vector3[n];
        var radii = new float[n];
        int ballIdx = -1;
        for (int i = 0; i < n; i++)
        {
            GpuSphere sp = table.Packed.Spheres[i];
            centers[i] = new Vector3(sp.CX, sp.CY, sp.CZ);
            radii[i] = sp.Radius;
            if (Vector3.Distance(centers[i], PinballTableScene.BallStart) < 1e-3f) ballIdx = i;
        }
        if (ballIdx < 0) { Console.WriteLine("  ball sphere not found"); return 1; }

        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.None);
        using var renderer = new Phase6Renderer(
            Width, Height, table.Packed, table.Spectral, table.PackedLights, table.Camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp,
            maxSampleCount: (uint)Math.Max(1, frames),
            biomeIndicator: false, debugMode: Phase5DebugMode.Beauty, decalAtlas: Phase6Renderer.EmptyDecalAtlas);
        renderer.Initialize(windowHandle: 0);
        renderer.SetCamera(table.Camera);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        // Time the mover frame cost (UpdateSpheres refit + trace/resolve), excluding frame 0 (reset/warmup),
        // so P4's AS-refit fold can be measured against the 16.6 ms (60 fps) budget.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int f = 0; f < frames; f++)
        {
            if (f == 1) sw.Restart(); // start timing after the warmup frame
            float t = frames > 1 ? f / (float)(frames - 1) : 1f;
            centers[ballIdx] = PinballTableScene.BallStart + new Vector3(4.2f * t, 0f, 0f); // sweep across +X
            renderer.UpdateSpheres(centers, radii);
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false); // camera fixed; only the ball moves
        }
        sw.Stop();
        int timed = Math.Max(1, frames - 1);
        double msPerFrame = sw.Elapsed.TotalMilliseconds / timed;
        Console.WriteLine($"  moving frame: {msPerFrame:0.00} ms/frame over {timed} frames -> {1000.0 / msPerFrame:0} fps ({(msPerFrame <= 16.6 ? ">=60fps" : "<60fps")})");

        byte[] rgba = renderer.ReadbackOutput();
        SavePng(rgba, Width, Height, savePath);
        Console.WriteLine($"Saved {Width}x{Height} PNG to {savePath}");
        return 0;
    }

    /// <summary>
    /// Headless <c>SetDynamicPose</c> demo / self-check (pinball-plan §4.2). A Plain-diffuse rectangular
    /// "flipper" quad tagged <see cref="IQuadPrimitive.Dynamic"/> rides its own movable TLAS instance (a
    /// dynamic triangle BLAS). It is converged at three poses and the SetDynamicPose plumbing is asserted,
    /// then the rotated frame is written as a PNG (plus <c>-identity</c>/<c>-baked</c> siblings). Checks:
    ///  • the dynamic BLAS + 3rd TLAS instance build and trace with no device-removal;
    ///  • a pose change moves the mover (identity ≠ rotated), and re-posing back to identity reproduces the
    ///    first frame (the transform upload + folded TLAS refit are clean and repeatable);
    ///  • <b>pose-invariance</b> — rotating a flat (+Y-normal, Plain) flipper by the instance transform
    ///    matches baking that same rotation into its world vertices (an independent scene). This is the real
    ///    correctness proof: it validates the transform math and the "normal/UV need no transform for a flat
    ///    Y-rotating mover" design against ground-truth geometry.
    /// </summary>
    private static int RunFlipperDemo(int frames, string savePath, float sampleClamp)
    {
        if (frames <= 0) frames = 96;
        const float angleDeg = 35f;
        float angle = angleDeg * MathF.PI / 180f;
        var pivot = new Vector3(0f, 1.0f, 4.0f); // the flipper's hinge (its −X end)
        Console.WriteLine($"Space Cadet RT — SetDynamicPose demo ({frames} frames, flipper {angleDeg:0}°) -> {savePath}");

        // Rotate a point about the world-Y axis through `pivot` (the flat-flipper hinge motion, §4.2).
        static Vector3 RotYAboutPivot(Vector3 p, Vector3 pivot, float a)
        {
            Vector3 d = p - pivot;
            float c = MathF.Cos(a), s = MathF.Sin(a);
            return pivot + new Vector3(c * d.X + s * d.Z, d.Y, -s * d.X + c * d.Z);
        }

        // The same rotation as a DXR 3×4 instance transform (row-major; the 4th column is translation):
        // p' = R·p + (pivot − R·pivot).
        static Vortice.Mathematics.Matrix3x4 RotYPoseAboutPivot(Vector3 pivot, float a)
        {
            float c = MathF.Cos(a), s = MathF.Sin(a);
            float tx = pivot.X * (1f - c) - pivot.Z * s;
            float tz = pivot.X * s + pivot.Z * (1f - c);
            return new Vortice.Mathematics.Matrix3x4(
                c, 0f, s, tx,
                0f, 1f, 0f, 0f,
                -s, 0f, c, tz);
        }
        var identity = new Vortice.Mathematics.Matrix3x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);

        // A lit box + one Plain-diffuse rectangular flipper (Dynamic) hovering above the floor. `bakedAngle`
        // != 0 pre-rotates the flipper's world verts (the pose-invariance ground truth); the dynamic scene
        // (bakedAngle 0) + a θ instance transform must then match the baked scene at identity.
        (PackedScene packed, SpectralResources spectral, PackedLights lights, Camera cam) BuildScene(float bakedAngle)
        {
            var scene = new List<Tracable>
            {
                FloorQuad(0f, -5f, 5f, -1f, 10f, FlatDiffuse("fd-floor", 0.28f)),
                FloorQuad(6f, -5f, 5f, -1f, 10f, FlatDiffuse("fd-ceil", 0.55f)),
                WallZ(9.5f, -5f, 5f, 0f, 6f, FlatDiffuse("fd-back", 0.7f)),
            };

            // The flipper: a horizontal rectangle (long in +X, short in +Z), bright red, Plain diffuse — no
            // UV dependence and its ±Y normal is invariant under the Y-rotation (§4.2 design assumption).
            var flipMat = ColoredDiffuse("fd-flip", new Vector3(0.85f, 0.09f, 0.09f));
            Vector3 a = new(0f, 1.0f, 3.6f), b = new(3.2f, 1.0f, 3.6f), c3 = new(0f, 1.0f, 4.4f);
            if (bakedAngle != 0f)
            {
                a = RotYAboutPivot(a, pivot, bakedAngle);
                b = RotYAboutPivot(b, pivot, bakedAngle);
                c3 = RotYAboutPivot(c3, pivot, bakedAngle);
            }
            scene.Add(new TracableRectangle((a, b, c3), flipMat) { Dynamic = true });

            var lights = new List<Light>
            {
                new() { Position = new Vector3(-1.5f, 5.2f, 3.0f), Color = Vector3.One },
                new() { Position = new Vector3(1.5f, 5.2f, 5.5f), Color = Vector3.One },
            };
            Vector3 camPos = new(1.6f, 5.6f, -1.2f);
            var camera = new Camera
            {
                Position = camPos,
                Rotation = LookRotation(camPos, new Vector3(1.6f, 0.6f, 4.2f), new Vector3(0, 1, 0)),
                Fov = MathF.PI / 3f,
                Aspect = (float)Width / Height,
                ImgPlaneZ = 1f,
            };
            PackedScene packed = GpuScenePacker.Pack(scene);
            SpectralResources spectral = SpectralResourceBaker.Bake(new WavelengthLookup(), packed.Materials);
            PackedLights packedLights = LightPacker.Pack(lights);
            return (packed, spectral, packedLights, camera);
        }

        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.None);
        Phase6Renderer MakeRenderer((PackedScene packed, SpectralResources spectral, PackedLights lights, Camera cam) s)
        {
            var r = new Phase6Renderer(
                Width, Height, s.packed, s.spectral, s.lights, s.cam,
                volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp,
                maxSampleCount: (uint)Math.Max(1, frames),
                biomeIndicator: false, debugMode: Phase5DebugMode.Beauty, decalAtlas: Phase6Renderer.EmptyDecalAtlas);
            r.Initialize(windowHandle: 0);
            r.SetCamera(s.cam);
            return r;
        }
        static byte[] Converge(Phase6Renderer r, int frames)
        {
            for (int f = 0; f < frames; f++)
                r.RenderHeadlessFrame(reset: f == 0, moving: false); // camera fixed; only the mover pose changes
            return (byte[])r.ReadbackOutput().Clone();
        }

        // ── Dynamic scene: one renderer, three poses (identity → rotated → identity). ──
        byte[] imgIdentity, imgRotated, imgIdentity2;
        double msPerFrame;
        using (var r = MakeRenderer(BuildScene(0f)))
        {
            Console.WriteLine($"Adapter: {r.AdapterName}");
            int di = 0; // this scene has a single dynamic mover part
            if (r.DynamicPartCount < 1) { Console.WriteLine("  FAIL: scene has no dynamic part (packer/renderer split)"); return 1; }
            Console.WriteLine($"  dynamic mover parts: {r.DynamicPartCount}");

            r.SetDynamicPose(di, identity);
            imgIdentity = Converge(r, frames);

            r.SetDynamicPose(di, RotYPoseAboutPivot(pivot, angle));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            imgRotated = Converge(r, frames);
            sw.Stop();
            msPerFrame = sw.Elapsed.TotalMilliseconds / frames;

            r.SetDynamicPose(di, identity); // re-pose back — must reproduce the first frame
            imgIdentity2 = Converge(r, frames);
        }

        // ── Ground truth: the flipper baked at θ in its world verts, rendered at identity. ──
        byte[] imgBaked;
        using (var r = MakeRenderer(BuildScene(angle)))
            imgBaked = Converge(r, frames);

        static (int over, int max) Diff(byte[] x, byte[] y, int tol)
        {
            int n = Math.Min(x.Length, y.Length), over = 0, max = 0;
            for (int i = 0; i < n; i++)
            {
                int d = Math.Abs(x[i] - y[i]);
                if (d > max) max = d;
                if (d > tol) over++;
            }
            return (over, max);
        }
        static string WithSuffix(string path, string suffix) => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(path) ?? "",
            System.IO.Path.GetFileNameWithoutExtension(path) + suffix + System.IO.Path.GetExtension(path));

        // Count strongly red-dominant pixels: the flipper's ColoredDiffuse red vs the grey floor/walls. This
        // INDEPENDENTLY verifies the InstanceID offset picks the flipper's own Primitives row — a dropped/
        // wrong offset makes a dynamic hit read Primitives[0] (the grey floor), so the flipper would render
        // grey and pose-invariance (which compares two dynamic paths) would still pass. This catches that.
        static int RedPixels(byte[] rgba)
        {
            int n = rgba.Length / 4, red = 0;
            for (int i = 0; i < n; i++)
            {
                int o = i * 4, r = rgba[o], g = rgba[o + 1], b = rgba[o + 2];
                if (r > g + 30 && r > b + 30) red++;
            }
            return red;
        }

        int totalBytes = Width * Height * 4;
        var (rtOver, rtMax) = Diff(imgIdentity, imgIdentity2, 2);
        var (moveOver, moveMax) = Diff(imgIdentity, imgRotated, 8);
        var (invOver, invMax) = Diff(imgRotated, imgBaked, 4);
        double rtPct = 100.0 * (totalBytes - rtOver) / totalBytes;
        double invPct = 100.0 * (totalBytes - invOver) / totalBytes;
        int redId = RedPixels(imgIdentity), redRot = RedPixels(imgRotated);

        SavePng(imgRotated, Width, Height, savePath);
        SavePng(imgIdentity, Width, Height, WithSuffix(savePath, "-identity"));
        SavePng(imgBaked, Width, Height, WithSuffix(savePath, "-baked"));

        Console.WriteLine($"  moving frame: {msPerFrame:0.00} ms/frame -> {1000.0 / msPerFrame:0} fps ({(msPerFrame <= 16.6 ? ">=60fps" : "<60fps")})");
        Console.WriteLine($"  re-pose round-trip (identity vs identity'): {rtPct:0.00}% within 2/255, max|Δ| {rtMax}");
        Console.WriteLine($"  pose moved (identity vs rotated):           {moveOver} bytes >8/255, max|Δ| {moveMax}");
        Console.WriteLine($"  pose-invariance (rotated vs baked θ):       {invPct:0.000}% within 4/255, max|Δ| {invMax}");
        Console.WriteLine($"  flipper shaded (red pixels id/rot):         {redId} / {redRot} (InstanceID offset picks its own material)");
        Console.WriteLine($"Saved {Width}x{Height} PNGs to {savePath} (+ -identity, -baked)");

        bool moved = moveOver > totalBytes / 200;             // the flipper visibly swept
        bool reposeClean = rtPct >= 99.0;                     // returning to identity restores the frame
        bool invariant = invPct >= 99.5 && invMax <= 16;      // transform == baked geometry (ground truth)
        bool shaded = redId > 3000 && redRot > 3000;          // reads the flipper's own row, not Primitives[0]
        bool pass = moved && reposeClean && invariant && shaded;
        Console.WriteLine(pass
            ? "  overall: PASS (dynamic BLAS + SetDynamicPose verified)"
            : $"  overall: FAIL (moved={moved} reposeClean={reposeClean} invariant={invariant} shaded={shaded})");
        return pass ? 0 : 1;
    }

    /// <summary>
    /// The P6 integration capstone (pinball-plan §6.2 / §8 P6): a <b>playable ball on the P0 table via the P4
    /// pose interface</b>, run headlessly. The deterministic physics core (<c>Pinball.Physics.PinballTable</c>,
    /// SI metres) drives the renderer each frame — the ball centre through <see cref="Phase6Renderer.UpdateSpheres"/>
    /// and each flipper's angle through <see cref="Phase6Renderer.SetDynamicPose"/> (two independent dynamic
    /// mover parts). Physics and render are co-registered by <c>PinballTable.RenderScale</c>. The ball is
    /// spawned over the left flipper and both flippers are energised mid-run, so the capture shows the ball
    /// caught and knocked back up-table. This is the seam the interactive WinForms host (the remaining P6
    /// productization) sits on.
    /// </summary>
    private static int RunPinballSim(int frames, string savePath, float sampleClamp)
    {
        if (frames <= 0) frames = 120;
        Console.WriteLine($"Space Cadet RT — pinball sim ({frames} frames) -> {savePath}");

        PinballTableScene table = PinballTableScene.Build(Width, Height, dynamicBall: true, dynamicFlippers: true);
        var physics = new Pinball.Physics.PinballTable();
        var world = new Pinball.Physics.PhysicsWorld(physics.Settings, physics.Colliders);
        world.Ball = Pinball.Physics.BallState.AtRest(Pinball.Physics.PinballTable.PlayfieldPoint(-2.1, 4.4)); // over the left flipper

        int n = table.Packed.Spheres.Count;
        var centers = new Vector3[n];
        var radii = new float[n];
        int ballIdx = -1;
        for (int i = 0; i < n; i++)
        {
            GpuSphere sp = table.Packed.Spheres[i];
            centers[i] = new Vector3(sp.CX, sp.CY, sp.CZ);
            radii[i] = sp.Radius;
            if (Vector3.Distance(centers[i], PinballTableScene.BallStart) < 1e-3f) ballIdx = i;
        }
        if (ballIdx < 0) { Console.WriteLine("  ball sphere not found"); return 1; }

        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.None);
        using var renderer = new Phase6Renderer(
            Width, Height, table.Packed, table.Spectral, table.PackedLights, table.Camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp,
            maxSampleCount: (uint)Math.Max(1, frames),
            biomeIndicator: false, debugMode: Phase5DebugMode.Beauty, decalAtlas: Phase6Renderer.EmptyDecalAtlas);
        renderer.Initialize(windowHandle: 0);
        renderer.SetCamera(table.Camera);
        Console.WriteLine($"Adapter: {renderer.AdapterName}; dynamic flipper parts: {renderer.DynamicPartCount}");
        if (renderer.DynamicPartCount != 2) { Console.WriteLine("  FAIL: expected 2 flipper parts"); return 1; }

        double scale = Pinball.Physics.PinballTable.RenderScale;
        Vector3 RenderPos(Pinball.Physics.Vector3D p) => new((float)(p.X / scale), (float)(p.Y / scale), (float)(p.Z / scale));
        Vector3 ballStartRender = RenderPos(world.Ball.Position);

        Pinball.Physics.Vector3D spawn = world.Ball.Position;
        float maxLeftAngle = 0, maxTravel = 0;
        int drains = 0;
        byte[]? flipFrame = null;
        for (int f = 0; f < frames; f++)
        {
            bool flip = f is >= 40 and < 80; // energise both flippers mid-run
            physics.LeftFlipper.Energized = flip;
            physics.RightFlipper.Energized = flip;
            for (int k = 0; k < 20; k++) world.Substep(); // ~20 ms of physics per rendered frame
            // Re-serve on drain so the ball stays on-table and visible (no front collider here) instead of
            // free-rolling off the low −z edge behind the camera.
            if (physics.IsDrained(world.Ball)) { world.Ball = Pinball.Physics.BallState.AtRest(spawn); drains++; }
            maxTravel = MathF.Max(maxTravel, Vector3.Distance(RenderPos(world.Ball.Position), ballStartRender));

            centers[ballIdx] = RenderPos(world.Ball.Position);
            renderer.UpdateSpheres(centers, radii);
            renderer.SetDynamicPose(0, RotYPose(table.LeftFlipperPivot, (float)physics.LeftFlipper.Angle));
            renderer.SetDynamicPose(1, RotYPose(table.RightFlipperPivot, (float)physics.RightFlipper.Angle));
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);

            maxLeftAngle = MathF.Max(maxLeftAngle, MathF.Abs((float)physics.LeftFlipper.Angle));
            if (f == 60) flipFrame = (byte[])renderer.ReadbackOutput().Clone(); // mid-flip
        }

        SavePng(renderer.ReadbackOutput(), Width, Height, savePath);
        if (flipFrame is not null)
            SavePng(flipFrame, Width, Height, System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(savePath) ?? "",
                System.IO.Path.GetFileNameWithoutExtension(savePath) + "-flip" + System.IO.Path.GetExtension(savePath)));

        Console.WriteLine($"  ball travel {maxTravel:0.00} render units; max left-flipper swing {maxLeftAngle:0.00} rad; drains re-served: {drains}");
        Console.WriteLine($"Saved {Width}x{Height} PNGs to {savePath} (+ -flip)");
        bool pass = maxLeftAngle > 1.0f && maxTravel > 0.3f;
        Console.WriteLine(pass ? "  overall: PASS (physics → render: ball via UpdateSpheres + 2 flippers via SetDynamicPose)" : "  overall: FAIL");
        return pass ? 0 : 1;
    }

    // A rotation-about-+Y-through-<paramref name="pivot"/> as a DXR 3×4 instance transform (row-major; 4th
    // column = translation): p' = R·p + (pivot − R·pivot). The flipper's physics angle drives its render pose.
    private static Vortice.Mathematics.Matrix3x4 RotYPose(Vector3 pivot, float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        float tx = pivot.X * (1f - c) - pivot.Z * s;
        float tz = pivot.X * s + pivot.Z * (1f - c);
        return new Vortice.Mathematics.Matrix3x4(c, 0f, s, tx, 0f, 1f, 0f, 0f, -s, 0f, c, tz);
    }

    /// <summary>
    /// A headless full-game playthrough (pinball-plan §6.2–§6.4 / §8 P6): the same physics→render seam as
    /// <c>--pinball-sim</c>, but driven through the <see cref="Pinball.Game.PinballGame"/> orchestrator on a
    /// fixed-timestep loop with a scripted input timeline (auto-launch each ball + a flipper pattern), so it
    /// exercises the whole lifecycle — serve → play → drain → next ball → game over — rendered on the P0
    /// table. This is what the interactive WinForms host (P6b productization) drives frame-by-frame.
    /// </summary>
    private static int RunPinballPlay(int frames, string savePath, float sampleClamp)
    {
        if (frames <= 0) frames = 160;
        Console.WriteLine($"Space Cadet RT — headless playthrough ({frames} frames) -> {savePath}");

        PinballTableScene table = PinballTableScene.Build(Width, Height, dynamicBall: true, dynamicFlippers: true);
        var game = new Pinball.Game.PinballGame();
        game.NewGame();

        int n = table.Packed.Spheres.Count;
        var centers = new Vector3[n];
        var radii = new float[n];
        int ballIdx = -1;
        for (int i = 0; i < n; i++)
        {
            GpuSphere sp = table.Packed.Spheres[i];
            centers[i] = new Vector3(sp.CX, sp.CY, sp.CZ);
            radii[i] = sp.Radius;
            if (Vector3.Distance(centers[i], PinballTableScene.BallStart) < 1e-3f) ballIdx = i;
        }
        if (ballIdx < 0) { Console.WriteLine("  ball sphere not found"); return 1; }

        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.None);
        using var renderer = new Phase6Renderer(
            Width, Height, table.Packed, table.Spectral, table.PackedLights, table.Camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp,
            maxSampleCount: (uint)Math.Max(1, frames),
            biomeIndicator: false, debugMode: Phase5DebugMode.Beauty, decalAtlas: Phase6Renderer.EmptyDecalAtlas);
        renderer.Initialize(windowHandle: 0);
        renderer.SetCamera(table.Camera);
        Console.WriteLine($"Adapter: {renderer.AdapterName}; dynamic flipper parts: {renderer.DynamicPartCount}");

        double scale = Pinball.Physics.PinballTable.RenderScale;
        Vector3 RenderPos(Pinball.Physics.Vector3D p) => new((float)(p.X / scale), (float)(p.Y / scale), (float)(p.Z / scale));

        int maxBall = 1;
        for (int f = 0; f < frames; f++)
        {
            var input = new Pinball.Game.PinballInput(
                LeftFlipper: (f / 15) % 2 == 0,
                RightFlipper: (f / 18) % 2 == 1,
                Launch: !game.State.BallInPlay); // auto-serve the next ball
            game.Tick(1.0 / 60.0, input);        // one 60 fps frame of game time

            centers[ballIdx] = RenderPos(game.Ball.Position);
            renderer.UpdateSpheres(centers, radii);
            renderer.SetDynamicPose(0, RotYPose(table.LeftFlipperPivot, (float)game.Table.LeftFlipper.Angle));
            renderer.SetDynamicPose(1, RotYPose(table.RightFlipperPivot, (float)game.Table.RightFlipper.Angle));
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);
            maxBall = Math.Max(maxBall, game.State.BallNumber);
        }

        SavePng(renderer.ReadbackOutput(), Width, Height, savePath);
        Console.WriteLine($"  progressed to ball {game.State.BallNumber} (reached {maxBall}); score {game.State.Score}; gameOver={game.State.GameOver}");
        Console.WriteLine($"Saved {Width}x{Height} PNG to {savePath}");
        bool pass = maxBall > 1 || game.State.GameOver; // the lifecycle advanced through drains
        Console.WriteLine(pass ? "  overall: PASS (game loop → render: serve/play/drain/next-ball)" : "  overall: FAIL (no ball progression)");
        return pass ? 0 : 1;
    }

    /// <summary>
    /// The interactive Space Cadet RT game window (pinball-plan §6.3 / §8 P6): a WinForms host on the same
    /// windowed <c>Phase6Renderer</c> seam the maze app uses, wiring keyboard input → the
    /// <see cref="Pinball.Game.PinballGame"/> orchestrator → the ray-traced renderer each frame. Real-time
    /// fixed-timestep physics (the game consumes the frame delta), the ball driven through
    /// <c>UpdateSpheres</c> and both flippers through <c>SetDynamicPose</c>. Controls: <b>Z</b>/LShift = left
    /// flipper, <b>/</b>/RShift = right, <b>Space</b> = launch / new game, <b>← → ↑</b> = nudge (mind the
    /// tilt), <b>Esc</b> = exit. Runs on a machine with a display + the DXR GPU (headless CI only compiles it).
    /// </summary>
    private static int RunPinballWindowed(float sampleClamp)
    {
        EnsureAppConfigured();
        PinballTableScene table = PinballTableScene.Build(Width, Height, dynamicBall: true, dynamicFlippers: true);
        var game = new Pinball.Game.PinballGame();
        game.NewGame();

        int n = table.Packed.Spheres.Count;
        var centers = new Vector3[n];
        var radii = new float[n];
        int ballIdx = -1;
        for (int i = 0; i < n; i++)
        {
            GpuSphere sp = table.Packed.Spheres[i];
            centers[i] = new Vector3(sp.CX, sp.CY, sp.CZ);
            radii[i] = sp.Radius;
            if (Vector3.Distance(centers[i], PinballTableScene.BallStart) < 1e-3f) ballIdx = i;
        }
        if (ballIdx < 0) { Console.WriteLine("  ball sphere not found"); return 1; }

        using var form = new Form
        {
            Text = "Space Cadet RT",
            ClientSize = new Size(Width, Height),
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
            KeyPreview = true,
        };
        bool left = false, right = false, launch = false, running = true;
        Vector3 nudge = default;
        form.FormClosed += (_, _) => running = false;
        form.Deactivate += (_, _) => { left = false; right = false; launch = false; }; // don't stick a held key on focus loss
        form.KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Escape: form.Close(); break;
                case Keys.Z: case Keys.LShiftKey: left = true; break;
                case Keys.OemQuestion: case Keys.RShiftKey: right = true; break;
                case Keys.Space: launch = true; break;
                case Keys.Left: nudge = new Vector3(-0.03f, 0, 0); break;
                case Keys.Right: nudge = new Vector3(0.03f, 0, 0); break;
                case Keys.Up: nudge = new Vector3(0, 0, 0.03f); break;
            }
        };
        form.KeyUp += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Z: case Keys.LShiftKey: left = false; break;
                case Keys.OemQuestion: case Keys.RShiftKey: right = false; break;
                case Keys.Space: launch = false; break;
            }
        };

        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.None);
        using var renderer = new Phase6Renderer(
            Width, Height, table.Packed, table.Spectral, table.PackedLights, table.Camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp, maxSampleCount: 2048,
            biomeIndicator: false, debugMode: Phase5DebugMode.Beauty, decalAtlas: Phase6Renderer.EmptyDecalAtlas);
        form.Show();
        renderer.Initialize(form.Handle);
        renderer.SetCamera(table.Camera);
        double scale = Pinball.Physics.PinballTable.RenderScale;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        double last = 0;
        uint frame = 0;
        bool launchPrev = false;
        while (running && !form.IsDisposed)
        {
            if (form.WindowState == FormWindowState.Minimized) { Application.DoEvents(); System.Threading.Thread.Sleep(15); continue; }

            double now = sw.Elapsed.TotalSeconds;
            double dt = Math.Min(now - last, 0.1); // clamp a stall so the accumulator can't explode
            last = now;

            // Edge-trigger launch/restart so HOLDING Space doesn't blow past GAME OVER or re-serve every frame.
            bool launchEdge = launch && !launchPrev;
            launchPrev = launch;
            var input = new Pinball.Game.PinballInput(left, right, launchEdge,
                new Pinball.Physics.Vector3D(nudge.X, nudge.Y, nudge.Z));
            if (game.State.GameOver && launchEdge) game.NewGame(); // a Space PRESS restarts after game over
            game.Tick(dt, input);
            nudge = default; // one-shot per press

            Pinball.Physics.Vector3D bp = game.Ball.Position;
            centers[ballIdx] = new Vector3((float)(bp.X / scale), (float)(bp.Y / scale), (float)(bp.Z / scale));
            renderer.UpdateSpheres(centers, radii);
            renderer.SetDynamicPose(0, RotYPose(table.LeftFlipperPivot, (float)game.Table.LeftFlipper.Angle));
            renderer.SetDynamicPose(1, RotYPose(table.RightFlipperPivot, (float)game.Table.RightFlipper.Angle));

            if (!renderer.RenderFrame(reset: frame == 0, moving: false))
            {
                if (!renderer.TryRecoverDevice()) break;
                frame = 0;
                continue;
            }
            frame++;
            if (frame % 15 == 0)
            {
                string status = game.State.GameOver ? " — GAME OVER (Space = new game)" : game.Tilted ? " — TILT" : "";
                form.Text = $"Space Cadet RT — Ball {game.State.BallNumber} — Score {game.State.Score}{status}  [Z / · Space · ←→↑ · Esc]";
            }
            Application.DoEvents();
        }
        return 0;
    }
}
