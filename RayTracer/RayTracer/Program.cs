using RayTracer;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;

namespace MiniRay;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // ── Build the maze scene (shared between calibration and gameplay) ──
        var materials = new MaterialsLookup();
        // Use a non-deterministic seed by default so each run produces a
        // different maze. This can be made configurable in the UI later.
        var maze = new Maze(16, 16, seed: Environment.TickCount);

        MaterialData? goalMaterial = null;
        if (materials.TryGetMaterial("01138", out var gm))
            goalMaterial = gm;

        materials.TryGetMaterial("01085", out var mortarMat);
        materials.TryGetMaterial("01138", out var ceilingGridMat);

        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],       // Red Brick
            floorMaterial: materials["01138"],       // Dark Red Brick (warm/dark, wood-ish)
            ceilingMaterial: materials["01085"],     // Grey Plaster (bright, marble-ish)
            mortarMaterial: mortarMat,
            ceilingGridMaterial: ceilingGridMat,
            goalMaterial: goalMaterial);

        var lights = MazeGeometryBuilder.BuildLights(maze);

        // Camera starting point (used for both calibration and gameplay).
        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        float eyeHeight = wh * 0.5f;
        Vector3 camPos = new(cs * 0.5f, eyeHeight, cs * 0.5f);

        var benchCamera = new Camera
        {
            Position = camPos,
            Rotation = CameraController.HeadingToQuaternion(Direction.South),
            Fov = MathF.PI / 3f,
            Aspect = 4f / 3f,
            ImgPlaneZ = 1f
        };

        // ── Calibrate and let the user pick a quality preset ──
        using var calibForm = new CalibrationForm(scene, benchCamera, lights);
        if (calibForm.ShowDialog() != DialogResult.OK)
            return;

        var preset = calibForm.ChosenPreset;

        // ── Launch the maze with the chosen settings ──
        Application.Run(new RayForm(preset, scene, maze, lights));
    }


}

public class RayForm : Form
{
    const int DisplayW = 800, DisplayH = 600;
    PictureBox _pb = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    Stopwatch _sw = new();
    Stopwatch _gsw = new();
    double _lastFrameElapsed;
    double _lastFrameMs;
    long _lastTotalRays;
    double _raysPerSecondInstant;
    System.Windows.Forms.Timer _timer;
    JobSystem jobSystem;
    CameraController camController;
    int stride;
    BitmapData data;
    Bitmap bmp;
    Rectangle rect;
    long _lastTileCompletions;
    Stopwatch _actualFpsSw = Stopwatch.StartNew();
    double _actualFps;
    readonly RenderPreset _preset;
    Font _hudFont;
    readonly Brush _hudBrush = Brushes.White;
    readonly Brush _hudShadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
    readonly Brush _panelLabelBg = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
    readonly Pen _panelBorderPen = new(Color.FromArgb(120, 255, 255, 255));
    readonly DebugViewMode[][] _debugPages =
    [
        // F1: Beauty (special - single fullscreen handled in ComposeFinalFrame)
        [DebugViewMode.Beauty, DebugViewMode.Beauty, DebugViewMode.Beauty, DebugViewMode.Beauty],
        // F2: Variance split: Beauty, Variance, Direct, Indirect
        [DebugViewMode.Beauty, DebugViewMode.Variance, DebugViewMode.DirectLighting, DebugViewMode.IndirectLighting],
        // F3: Current vs Accumulated: Beauty, CurrentVsAccumDiff, UnfilteredVsFilteredDiff, ReprojectedVsCurrentDiff
        [DebugViewMode.Beauty, DebugViewMode.CurrentVsAccumDiff, DebugViewMode.UnfilteredVsFilteredDiff, DebugViewMode.ReprojectedVsCurrentDiff],
        // F4: Clamp heatmap: Beauty, ClampHeatmap, RejectionMask, Variance
        [DebugViewMode.Beauty, DebugViewMode.ClampHeatmap, DebugViewMode.RejectionMask, DebugViewMode.Variance],
        // F5: Bounce breakdown: Beauty, Bounce0, Bounce1, Bounce2Plus
        [DebugViewMode.Beauty, DebugViewMode.Bounce0, DebugViewMode.Bounce1, DebugViewMode.Bounce2Plus],
        // F6: History age/weight: Beauty, HistoryWeight, ReprojectedVsCurrentDiff, UnfilteredVsFilteredDiff
        [DebugViewMode.Beauty, DebugViewMode.HistoryWeight, DebugViewMode.ReprojectedVsCurrentDiff, DebugViewMode.UnfilteredVsFilteredDiff],
        // F7: Reuse / cache: Beauty, RejectionMask, SampleCount, Variance
        [DebugViewMode.Beauty, DebugViewMode.RejectionMask, DebugViewMode.SampleCount, DebugViewMode.Variance],
        // F8: Edge disagreement: Beauty, ReprojectedVsCurrentDiff, Depth, Normal
        [DebugViewMode.Beauty, DebugViewMode.ReprojectedVsCurrentDiff, DebugViewMode.Depth, DebugViewMode.Normal]
    ];
    int _activeDebugPage = 0;
    bool _showDebugPage = true;
    bool _beautyOnly = false;
    bool _showHud = true;
    bool _overlayEnabled = true;
    Font _hudFontLarge;
    Bitmap _presentBmp;
    byte[] _debugScratchBuffer;
    int _pickedX = -1;
    int _pickedY = -1;
    bool _wasTurningLastFrame;

    public RayForm(RenderPreset preset, Tracable[] scene, Maze maze, Light[] lights)
    {
        _preset = preset;
        int W = preset.EffectiveWidth;
        int H = preset.EffectiveHeight;

        _gsw.Start();
        // Start a high-resolution stopwatch to measure frame-to-frame time.
        _sw = Stopwatch.StartNew();
        _lastFrameElapsed = _sw.Elapsed.TotalSeconds;
        Text = $"CPU Ray Tracer – {preset.Name} ({W}×{H}, {preset.FeatureSummary})";
        ClientSize = new Size(DisplayW, DisplayH);
        Controls.Add(_pb);

        // ~120Hz timer to trigger redraw
        _timer = new System.Windows.Forms.Timer();
        _timer.Interval = 1000 / 120;
        _timer.Tick += (__, _) => Render();
        _timer.Start();

        // Position the camera at eye height inside cell (0,0).
        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        float eyeHeight = wh * 0.5f;
        Vector3 camPos = new(cs * 0.5f, eyeHeight, cs * 0.5f);

        // Set up first-person navigation: start at cell (0,0) heading South (+Z).
        var navigator = new MazeNavigator(maze, 0, 0, Direction.South);
        camController = new CameraController(navigator, cs, eyeHeight);

        var camera = new Camera()
        {
            Position = camPos,
            Rotation = CameraController.HeadingToQuaternion(Direction.South),
            Fov = MathF.PI / 3f,
            Aspect = (float)W / H,
            ImgPlaneZ = 1f
        };

        // Lock the bitmap for fast writes
        bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        _presentBmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        rect = new Rectangle(0, 0, W, H);
        data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        stride = data.Stride;
        bmp.UnlockBits(data);
        _debugScratchBuffer = new byte[stride * H];

        jobSystem = new JobSystem(
            W,
            H,
            scene,
            camera,
            stride,
            lights,
            renderOptions: new RenderOptions(
                TileSize: preset.TileSize,
                SppPerJob: preset.SppPerJob,
                MaxSampleCount: preset.MaxSampleCount,
                Lighting: preset.Lighting,
                ThrottleCpu: preset.ThrottleCpu),
            samplingOptions: new SamplingOptions(
                MotionSampleCap: preset.MotionSampleCap,
                SubPixelJitter: preset.SubPixelJitter),
            denoiseOptions: new DenoiseOptions(
                FilterRadius: preset.FilterRadius,
                EdgeAwareFilter: preset.EdgeAwareFilter,
                CheckerboardMotion: preset.CheckerboardMotion,
                TemporalBlendAlpha: preset.TemporalBlendAlpha,
                EnableTaa: preset.TemporalBlendAlpha > 0f,
                SampleClamp: preset.SampleClamp),
            debugOptions: new DebugOptions());

        jobSystem.SetupJobs(CancellationToken.None);
        jobSystem.AddJobs();

        // Pixel picker: store clicked pixel coords for HUD inspection
        _pb.MouseClick += (s, e) =>
        {
            if (_presentBmp is null) return;
            // Map mouse position to image pixel coordinates considering SizeMode.Zoom
            var pb = (PictureBox)s;
            var img = pb.Image;
            if (img == null) return;

            // Compute displayed image rectangle inside the PictureBox
            var clientSize = pb.ClientSize;
            float imgAspect = (float)img.Width / img.Height;
            float boxAspect = clientSize.Width / (float)clientSize.Height;
            int drawW, drawH, offsetX, offsetY;
            if (imgAspect > boxAspect)
            {
                drawW = clientSize.Width;
                drawH = (int)(clientSize.Width / imgAspect);
                offsetX = 0;
                offsetY = (clientSize.Height - drawH) / 2;
            }
            else
            {
                drawH = clientSize.Height;
                drawW = (int)(clientSize.Height * imgAspect);
                offsetY = 0;
                offsetX = (clientSize.Width - drawW) / 2;
            }

            int mx = e.X - offsetX;
            int my = e.Y - offsetY;
            if (mx < 0 || my < 0 || mx >= drawW || my >= drawH)
            {
                _pickedX = -1;
                _pickedY = -1;
                return;
            }

            int ix = (int)MathF.Floor(mx * (float)img.Width / drawW);
            int iy = (int)MathF.Floor(my * (float)img.Height / drawH);
            ix = Math.Clamp(ix, 0, img.Width - 1);
            iy = Math.Clamp(iy, 0, img.Height - 1);
            _pickedX = ix;
            _pickedY = iy;
        };

        // Create HUD fonts sized relative to the chosen render resolution
        // baseSize scales roughly with the smaller dimension of the render target
        float baseSize = MathF.Max(6f, MathF.Min(14f, MathF.Min(jobSystem.Width, jobSystem.Height) / 40f));
        _hudFont = new Font("Consolas", baseSize, FontStyle.Regular, GraphicsUnit.Point);
        _hudFontLarge = new Font("Consolas", MathF.Max(12f, baseSize * 1.8f), FontStyle.Regular, GraphicsUnit.Point);

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F1) { _activeDebugPage = 0; _beautyOnly = false; _showDebugPage = false; }
            else if (e.KeyCode == Keys.F2) { _activeDebugPage = 1; _beautyOnly = false; _showDebugPage = true; }
            else if (e.KeyCode == Keys.F3) { _activeDebugPage = 2; _beautyOnly = false; _showDebugPage = true; }
            else if (e.KeyCode == Keys.F4) { _activeDebugPage = 3; _beautyOnly = false; _showDebugPage = true; }
            else if (e.KeyCode == Keys.F5) { _activeDebugPage = 4; _beautyOnly = false; _showDebugPage = true; }
            else if (e.KeyCode == Keys.F6) { _activeDebugPage = 5; _beautyOnly = false; _showDebugPage = true; }
            else if (e.KeyCode == Keys.F7) { _activeDebugPage = 6; _beautyOnly = false; _showDebugPage = true; }
            else if (e.KeyCode == Keys.F8) { _activeDebugPage = 7; _beautyOnly = false; _showDebugPage = true; }
            else if (e.KeyCode == Keys.F9) { _showHud = true; }
            else if (e.KeyCode == Keys.F10) { _showHud = false; }
            else if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D8)
            {
                int idx = (int)e.KeyCode - (int)Keys.D1;
                _activeDebugPage = Math.Clamp(idx, 0, _debugPages.Length - 1);
                _beautyOnly = false;
                _showDebugPage = true;
            }
            else if (e.KeyCode == Keys.F5)
            {
                // Set beauty-only mode (do not toggle). F1-F8 will exit beauty mode.
                _beautyOnly = true;
                _showDebugPage = false;
            }
        };
    }

    void Render()
    {
        // Compute high-resolution delta time since last frame. Using a
        // continuously running Stopwatch and subtracting the previous
        // timestamp avoids timer quantization and keeps movement speed
        // independent of the UI timer frequency.
        double current = _sw.Elapsed.TotalSeconds;
        float dt = (float)(current - _lastFrameElapsed);
        if (dt > 0f)
            camController.Update(dt, jobSystem.Camera);

        bool isTurning = camController.IsTurning;
        jobSystem.IsTurning = isTurning;

        // If the camera moved, cap sample counts and enable spatial
        // denoising so the image stays smooth during motion.
        if (camController.Dirty)
        {
            jobSystem.IsMoving = true;
            // Always apply a soft reset so sample counts are capped while moving.
            jobSystem.SoftResetAccumulation();

            // For in-place turns, invalidate TAA history once when entering a turn.
            if (isTurning && !_wasTurningLastFrame)
                jobSystem.InvalidateTaaHistory();

            camController.Dirty = false;
        }
        else
        {
            jobSystem.IsMoving = false;
        }

        _wasTurningLastFrame = isTurning;

        jobSystem.ResolveDisplayBufferWithTaa();

        data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        stride = data.Stride;

        // Copy buffer into bitmap
        Marshal.Copy(jobSystem.DisplayBuffer, 0, data.Scan0, jobSystem.DisplayBuffer.Length);
        bmp.UnlockBits(data);
        ComposeFinalFrame();

        // Instantaneous frame time and FPS based on the high-resolution delta.
        double ms = Math.Max(0.0, (current - _lastFrameElapsed) * 1000.0);
        double fps = ms > 0.0 ? 1000.0 / ms : 0.0;

        // Compute actual full-screen passes per second
        long currentTiles = jobSystem.TotalTileCompletions;
        double elapsedSec = _actualFpsSw.Elapsed.TotalSeconds;
        if (elapsedSec >= 0.5) // update every 0.5s for a stable reading
        {
            long deltaTiles = currentTiles - _lastTileCompletions;
            double deltaPasses = (double)deltaTiles / jobSystem.TotalTiles;
            _actualFps = deltaPasses / elapsedSec;
            _lastTileCompletions = currentTiles;
            _actualFpsSw.Restart();
        }

        // Instantaneous rays/sec based on rays traced since last frame.
        var totalRays = jobSystem.TotalRays;
        double deltaRays = (double)(totalRays - _lastTotalRays);
        double deltaSecForRays = Math.Max(0.0001, current - _lastFrameElapsed);
        _raysPerSecondInstant = deltaRays / deltaSecForRays;
        _lastTotalRays = totalRays;

        Text = $"CPU Ray Tracer – {_preset.Name} | {fps:F0} timer FPS, {_actualFps:F1} actual FPS, {_raysPerSecondInstant:#,##0} rays/s";

        // Advance the last-frame timestamp for the next tick and record ms
        // for HUD display.
        _lastFrameElapsed = current;
        _lastFrameMs = ms;
    }

    private void ComposeFinalFrame()
    {
        using var gfx = Graphics.FromImage(_presentBmp);
        gfx.Clear(Color.Black);
        gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        gfx.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        // Compose content first (beauty or debug panels). Draw HUD afterwards
        // so it overlays on top of the panels and is always visible when
        // toggled with F9.
        if (!_showDebugPage)
        {
            // Beauty mode: draw the beauty image across the present bitmap.
            var contentRect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            gfx.DrawImage(bmp, contentRect);
            gfx.DrawRectangle(_panelBorderPen, new Rectangle(0, 0, bmp.Width, bmp.Height));
            // Draw small label near top-left
            gfx.DrawString("Beauty", _showHud ? _hudFontLarge : _hudFont, _hudBrush, 8, 8);
            _pb.Image = _presentBmp;
            // HUD will be drawn below if enabled
        }
        else
        {
            // Layout four debug panels while preserving the aspect ratio of the
            // selected render resolution.
            int contentW = bmp.Width;
            int contentH = bmp.Height;
            float targetAspect = jobSystem.Width / (float)jobSystem.Height; // W/H of render target

            // Determine maximum panel height based on contentH/2 and width based on contentW/2
            float ph = Math.Min(contentH / 2f, (contentW / 2f) / targetAspect);
            float pw = targetAspect * ph;

            int piw = Math.Max(1, (int)MathF.Round(pw));
            int pih = Math.Max(1, (int)MathF.Round(ph));

            int totalW = piw * 2;
            int totalH = pih * 2;

            int offsetX = (contentW - totalW) / 2;
            int offsetY = (contentH - totalH) / 2;

            var slots = _debugPages[Math.Clamp(_activeDebugPage, 0, _debugPages.Length - 1)];
            Rectangle[] rects =
            [
                new Rectangle(offsetX, offsetY, piw, pih),
                new Rectangle(offsetX + piw, offsetY, piw, pih),
                new Rectangle(offsetX, offsetY + pih, piw, pih),
                new Rectangle(offsetX + piw, offsetY + pih, piw, pih)
            ];

            for (int i = 0; i < 4; i++)
            {
                DrawPanel(gfx, slots[i], rects[i]);
            }

            _pb.Image = _presentBmp;
        }
        // Draw HUD last so it overlays content
        if (_showHud)
            DrawHud(gfx);
    }

    private void DrawPanel(Graphics gfx, DebugViewMode mode, Rectangle dest)
    {
        if (mode == DebugViewMode.Beauty)
        {
            gfx.DrawImage(bmp, dest);
        }
        else
        {
            jobSystem.RenderDebugModeToBuffer(mode, _debugScratchBuffer, stride);
            using var panelBmp = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
            var panelData = panelBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(_debugScratchBuffer, 0, panelData.Scan0, _debugScratchBuffer.Length);
            panelBmp.UnlockBits(panelData);
            gfx.DrawImage(panelBmp, dest);
        }

        gfx.DrawRectangle(_panelBorderPen, dest);
        var label = mode.ToString();
        var labelFont = _showHud ? _hudFontLarge : _hudFont;
        var size = gfx.MeasureString(label, labelFont);
        var labelRect = new Rectangle(dest.X + 6, dest.Y + 6, (int)size.Width + 8, (int)size.Height + 4);
        gfx.FillRectangle(_panelLabelBg, labelRect);
        gfx.DrawString(label, labelFont, _hudBrush, dest.X + 9, dest.Y + 7);
    }

    private int DrawHud(Graphics g)
    {
        var totalRays = jobSystem.TotalRays;
        var raysPerSecond = totalRays / Math.Max(0.001f, _gsw.ElapsedMilliseconds / 1000f);
        var pageId = _activeDebugPage + 1;
        var activeMode = _debugPages[Math.Clamp(_activeDebugPage, 0, _debugPages.Length - 1)][1];
        if (!_showHud)
            return 0;

        string[] lines =
        [
            $"Preset {_preset.Name} | {jobSystem.Width}x{jobSystem.Height} | Frame {_lastFrameMs:0.0} ms",
            $"Actual FPS {_actualFps:0.0} | Rays/sec {_raysPerSecondInstant:#,##0} | Frame {jobSystem.FrameIndex}",
            $"SPP/job {jobSystem.SppPerJob} | Avg SPP {jobSystem.AverageEffectiveSpp:0.0} | Max SPP {jobSystem.MaxObservedSampleCount}",
            $"Moving {jobSystem.IsMoving} | TAA alpha {jobSystem.TemporalBlendAlpha:0.00} | Motion cap {jobSystem.MotionSampleCap}",
            $"Filter r={jobSystem.FilterRadius} edgeAware={jobSystem.EdgeAwareFilter} checker={jobSystem.CheckerboardMotion}",
            $"Clamp {jobSystem.SampleClamp:0.0} | Reject {jobSystem.RejectedHistoryPercent:0.0}% | Clamped {jobSystem.ClampedPixelPercent:0.0}%",
            $"Avg variance {jobSystem.AverageVariance:0.0000} | Avg history {jobSystem.AverageHistoryWeight:0.000}",
            $"Page F{pageId} | {jobSystem.GetDebugLegend(activeMode)}"
        ];

        // If the user picked a pixel, append its debug details.
        if (_pickedX >= 0 && _pickedY >= 0)
        {
            var info = jobSystem.GetPixelDebugInfo(_pickedX, _pickedY);
            var pickLines = new string[]
            {
                $"Picked ({_pickedX},{_pickedY}) Samples={info.SampleCount} Clamp={info.ClampAmount:0.000} ClampHit={info.ClampHit}",
                $"CurrVsAccumDiff={info.CurrentVsAccumDiff:0.000} HistWeight={info.HistoryWeight:0.000} Rejected={info.HistoryRejected}",
                $"Depth={info.Depth:0.00} Albedo={info.Albedo:0.000} Normal=({info.Normal.X:0.00},{info.Normal.Y:0.00},{info.Normal.Z:0.00})",
                $"AccumRGB=({info.AccumulatedXYZ.X:0.000},{info.AccumulatedXYZ.Y:0.000},{info.AccumulatedXYZ.Z:0.000})",
                $"FilteredRGB=({info.FilteredXYZ.X:0.000},{info.FilteredXYZ.Y:0.000},{info.FilteredXYZ.Z:0.000})"
            };

            var newLines = new string[lines.Length + pickLines.Length];
            lines.CopyTo(newLines, 0);
            pickLines.CopyTo(newLines, lines.Length);
            lines = newLines;
        }

        int x = 8;
        int y = 8;
        int width = Math.Min(720, bmp.Width - 16);
        int lineHeight = _showHud ? Math.Max(18, (int)(_hudFontLarge.SizeInPoints * 1.6f)) : Math.Max(12, (int)(_hudFont.SizeInPoints * 1.6f));
        int height = lines.Length * lineHeight + 8;
        g.FillRectangle(_hudShadowBrush, new Rectangle(x, y, width, height));

        var useFont = _showHud ? _hudFontLarge : _hudFont;
        for (int i = 0; i < lines.Length; i++)
            g.DrawString(lines[i], useFont, _hudBrush, x + 6, y + 4 + i * lineHeight);
        return y + height;
    }

    protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, Keys keyData)
    {
        // Ensure function keys are handled at the form level even when a child control has focus.
        if (keyData == Keys.F1) { _activeDebugPage = 0; _beautyOnly = false; _showDebugPage = false; return true; }
        if (keyData == Keys.F2) { _activeDebugPage = 1; _beautyOnly = false; _showDebugPage = true; return true; }
        if (keyData == Keys.F3) { _activeDebugPage = 2; _beautyOnly = false; _showDebugPage = true; return true; }
        if (keyData == Keys.F4) { _activeDebugPage = 3; _beautyOnly = false; _showDebugPage = true; return true; }
        if (keyData == Keys.F5) { _activeDebugPage = 4; _beautyOnly = false; _showDebugPage = true; return true; }
        if (keyData == Keys.F6) { _activeDebugPage = 5; _beautyOnly = false; _showDebugPage = true; return true; }
        if (keyData == Keys.F7) { _activeDebugPage = 6; _beautyOnly = false; _showDebugPage = true; return true; }
        if (keyData == Keys.F8) { _activeDebugPage = 7; _beautyOnly = false; _showDebugPage = true; return true; }
        if (keyData == Keys.F9) { _showHud = true; return true; }
        if (keyData == Keys.F10) { _showHud = false; return true; }
        if (keyData == Keys.F5) { _beautyOnly = true; _showDebugPage = false; return true; }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
