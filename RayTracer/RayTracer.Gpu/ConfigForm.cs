using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Overall render look. <see cref="Enhanced"/> is the project's showcase (spectral
/// path tracing with NEE lighting, indirect bounces, and volumetrics);
/// <see cref="Classic"/> chases the original Windows <i>3D Maze</i> screensaver — the
/// same spectral tracer run <b>unlit</b> (fullbright albedo, no smoke), which is the
/// spectral equivalent of the original's flat texture-mapped corridors.
/// </summary>
public enum RenderStyle
{
    /// <summary>Original look: unlit fullbright spectral, smoke off.</summary>
    Classic,

    /// <summary>Today's look: NEE lighting + indirect + volumetrics.</summary>
    Enhanced,
}

/// <summary>Which renderer backend the config screen launches.</summary>
internal enum RenderBackend
{
    /// <summary>Hardware DXR path — the Phase 6 renderer.</summary>
    Gpu,

    /// <summary>CPU software path — <see cref="RayForm"/>.</summary>
    Cpu,
}

/// <summary>How much each movie frame is sampled (see <c>Program.CaptureMovieFrames</c>). Both modes bake
/// every frame independently (accumulation reset per frame, so nothing ghosts and the noise level is uniform);
/// they differ only in the per-frame time budget preset.</summary>
internal enum MovieQualityMode
{
    /// <summary>Small per-frame budget — fast to bake, a bit noisy, but consistent (no temporal ghosting).</summary>
    Realtime,

    /// <summary>Large per-frame budget — keep accumulating until it converges. Clean, but much slower.</summary>
    Beautiful,
}

/// <summary>How the length of a recorded movie is decided (see <c>Program.CaptureMovieFrames</c>).</summary>
internal enum MovieLengthMode
{
    /// <summary>Skip a start offset, then record a fixed number of seconds.</summary>
    Duration,

    /// <summary>Record whole walks — each from the maze build-in until the walker reaches the goal and the
    /// completion fade finishes — for a set number of walks.</summary>
    Walks,
}

/// <summary>
/// Persisted CPU "Custom" preset knobs, used when <see cref="AppSettings.CpuPreset"/>
/// is <c>"Custom"</c>. Mirrors the tunable fields of <see cref="RenderPreset"/> that
/// the original CPU app's CalibrationForm advanced panel exposed.
/// </summary>
internal sealed class CpuCustomSettings
{
    public int RenderWidth { get; set; } = 480;
    public int RenderHeight { get; set; } = 360;
    public float ResolutionScale { get; set; } = 1.0f;
    public int SppPerJob { get; set; } = 4;
    public uint MaxSampleCount { get; set; } = 300;
    public uint MotionSampleCap { get; set; } = 16;
    public int FilterRadius { get; set; } = 1;
    public int TileSize { get; set; } = 32;
    public LightingMode Lighting { get; set; } = LightingMode.Direct;
    public SmokeMode SmokeMode { get; set; } = SmokeMode.Biome;
    public VolumetricQuality VolumetricQuality { get; set; } = VolumetricQuality.Medium;
    public bool SubPixelJitter { get; set; } = true;
    public bool EdgeAwareFilter { get; set; } = true;
    public bool CheckerboardMotion { get; set; } = true;
    public bool TemporalEma { get; set; } = true;
    public bool SampleClamp { get; set; }

    public CpuCustomSettings Clone() => (CpuCustomSettings)MemberwiseClone();
}

/// <summary>
/// App-wide, persisted settings shared by the normal launch (config-then-run, see
/// <c>Program.RunConfigApp</c>) and the screensaver switches (<see cref="Screensaver"/>).
/// Saved as JSON under <c>%APPDATA%\RayTracer.Gpu</c> so a choice made in the config
/// screen survives across launches. Carries both the GPU knobs and the CPU
/// backend selection (<see cref="Backend"/> + <see cref="CpuPreset"/>).
/// </summary>
internal sealed class AppSettings
{
    /// <summary>Which renderer the config screen launches. Screensaver mode is always GPU.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RenderBackend Backend { get; set; } = RenderBackend.Gpu;

    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool Fullscreen { get; set; }

    /// <summary>Open-air garden half (outdoor-area-plan.md): the far half of the maze becomes roofless,
    /// hedged, and grassed under a Rayleigh sky. On by default; Classic look forces it off (see the
    /// config screen) so the retro look stays fully indoors.</summary>
    public bool OutdoorArea { get; set; } = true;

    /// <summary>Overall look (Classic unlit vs. Enhanced spectral). See <see cref="RenderStyle"/>.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RenderStyle Style { get; set; } = RenderStyle.Enhanced;

    public SmokeMode SmokeMode { get; set; } = SmokeMode.Biome;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Phase5DebugMode StartView { get; set; } = Phase5DebugMode.Beauty;

    public int MazeSize { get; set; } = 16;
    public float FogDrift { get; set; } = 1f;
    public float SampleClamp { get; set; } = 3f;
    public float StillTime { get; set; } = 1.5f;

    // ── GPU fog / day-night / convergence (were hardcoded in RunPhase6Windowed) ──
    /// <summary>GPU fog quality tier: march steps, geometry occlusion, self-shadowing, and density.
    /// Higher tiers occlude the fog against walls; the app forces occlusion on at every non-Off tier.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VolumetricQuality VolumetricQuality { get; set; } = VolumetricQuality.Medium;

    /// <summary>Animate the sun through the day/night cycle in the outdoor garden (§O8). Off → a static
    /// midday sky (the built-in sun), no moving sun or clock.</summary>
    public bool DayNightCycle { get; set; } = true;

    /// <summary>Seconds for one full dawn→noon→dusk→night loop when <see cref="DayNightCycle"/> is on.</summary>
    public float DayLengthSeconds { get; set; } = 180f;

    // GPU renderer convergence / denoise knobs — the "Advanced" panel. Defaults mirror Phase6Renderer's.
    public uint MaxSampleCount { get; set; } = 2048;
    public uint MotionSampleCap { get; set; } = 12;
    public int FilterRadius { get; set; } = 1;
    public bool SubPixelJitter { get; set; } = true;
    public float TemporalBlendAlpha { get; set; } = 0.1f;

    // ── CPU backend selection ──
    /// <summary>Named CPU quality preset, or "Custom" (then see <see cref="CpuCustom"/>).</summary>
    public string CpuPreset { get; set; } = "Medium";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CpuThrottle CpuThrottle { get; set; } = CpuThrottle.Normal;

    public CpuCustomSettings CpuCustom { get; set; } = new();

    // ── Classic props & animations (orthogonal to Style — available in every look).
    //    Defaults match the original screensaver: props on, overhead map off. ──
    public bool ShowRat { get; set; } = true;
    public bool ShowOpenGlLogo { get; set; } = true;
    public bool ShowWallSigns { get; set; } = true;
    public bool ShowOverheadMap { get; set; }
    public bool BumpyWalls { get; set; } = true;

    /// <summary>Jewel caustics (shadows-and-caustics-plan §B4): the floating crystals throw a coloured
    /// caustic onto the floor. OFF by default — the forward photon map is built on the CPU, which
    /// briefly hitches startup and every maze regeneration (the GPU compute-pass port removes that).</summary>
    public bool JewelCaustics { get; set; }

    /// <summary>Classic-only depth cue (§1.4): fade corridors toward dark with distance. Off by
    /// default so Classic stays "spectral, just unlit" unless the user opts in.</summary>
    public bool ClassicDepthCue { get; set; }

    /// <summary>Classic-only retro pixelation (§1.5): render the beauty chunky/low-res for the 90s
    /// look. Off by default.</summary>
    public bool RetroPixelation { get; set; }

    /// <summary>Walls rise from the floor when a maze starts.</summary>
    public bool MazeBuildInAnim { get; set; } = true;

    /// <summary>On reaching the goal, generate a fresh maze and keep going.</summary>
    public bool MazeRegenerate { get; set; } = true;

    /// <summary>Play a short completion transition before regenerating.</summary>
    public bool MazeOutroAnim { get; set; } = true;

    // ── Movie recording (GPU only) ──
    /// <summary>Record the autonomous maze walk to an MP4 (via ffmpeg) instead of opening the live window.
    /// The scene is rendered headless: each output frame accumulates <see cref="MovieSpp"/> converged
    /// samples, so the movie is far cleaner than the live temporal view. Off → normal live launch.</summary>
    public bool RecordMovie { get; set; }

    /// <summary>Seconds of the walk to run (unrecorded) before capture begins — lets the maze build-in play
    /// out and the walker get going before the movie starts.</summary>
    public float MovieStartSeconds { get; set; } = 5f;

    /// <summary>Seconds of footage to capture once recording starts (<see cref="MovieLengthMode.Duration"/>).
    /// Spanning a maze completion just keeps recording into the next (regenerated) maze.</summary>
    public float MovieDurationSeconds { get; set; } = 30f;

    /// <summary>Output frame rate. Both the capture timestep and the ffmpeg playback rate, so playback speed
    /// matches the simulated walk.</summary>
    public int MovieFps { get; set; } = 30;

    /// <summary>Quality preset — real-time (small per-frame budget, fast) vs. beautiful (large budget, clean).
    /// Both bake each frame independently; the mode just seeds <see cref="MovieBakeMs"/>.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MovieQualityMode MovieQuality { get; set; } = MovieQualityMode.Beautiful;

    /// <summary>Per-frame render budget in milliseconds: keep accumulating samples on each frame until this
    /// elapses (also capped by <see cref="MaxSampleCount"/>). Higher → cleaner but slower. ~150ms is a fast
    /// preview, ~400ms+ converges.</summary>
    public float MovieBakeMs { get; set; } = 300f;

    /// <summary>Record a fixed duration, or a set number of complete walks.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MovieLengthMode MovieLengthMode { get; set; } = MovieLengthMode.Duration;

    /// <summary>Number of complete walks to record when <see cref="MovieLengthMode"/> is
    /// <see cref="MovieLengthMode.Walks"/>.</summary>
    public int MovieWalks { get; set; } = 1;

    /// <summary>Burn the per-frame render rate into each frame — small in beautiful mode, large in
    /// real-time — so a clip is honest about how it was produced.</summary>
    public bool MovieShowFps { get; set; } = true;

    /// <summary>Edge-aware denoise applied only to the final Beautiful movie frame (in software, after
    /// readback). Heavy views (Ultra fog + caustics + bumpy walls + spectral) stay grainy even after
    /// hundreds of samples; this cleans the residual grain while keeping mortar lines, signs and
    /// silhouettes crisp. Never touches the shader resolve, so real-time and the goldens are unaffected.</summary>
    public bool MovieDenoise { get; set; } = true;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RayTracer.Gpu", "settings.json");

    /// <summary>A small, recent render thumbnail — refreshed by the live GPU app and by movie capture —
    /// shown in the config screen's Record-movie box so the user can see what the walk currently looks
    /// like. Deliberately allowed to be slightly stale (it's whatever the last render left).</summary>
    internal static string PreviewImagePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RayTracer.Gpu", "preview.png");

    public static AppSettings Load()
    {
        try
        {
            string path = SettingsPath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt / unreadable settings fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort; a failed save just means defaults next time.
        }
    }

    public AppSettings Clone() => new()
    {
        Backend = Backend,
        Width = Width,
        Height = Height,
        Fullscreen = Fullscreen,
        OutdoorArea = OutdoorArea,
        Style = Style,
        SmokeMode = SmokeMode,
        StartView = StartView,
        MazeSize = MazeSize,
        FogDrift = FogDrift,
        SampleClamp = SampleClamp,
        StillTime = StillTime,
        VolumetricQuality = VolumetricQuality,
        DayNightCycle = DayNightCycle,
        DayLengthSeconds = DayLengthSeconds,
        MaxSampleCount = MaxSampleCount,
        MotionSampleCap = MotionSampleCap,
        FilterRadius = FilterRadius,
        SubPixelJitter = SubPixelJitter,
        TemporalBlendAlpha = TemporalBlendAlpha,
        CpuPreset = CpuPreset,
        CpuThrottle = CpuThrottle,
        CpuCustom = CpuCustom.Clone(),
        ShowRat = ShowRat,
        ShowOpenGlLogo = ShowOpenGlLogo,
        ShowWallSigns = ShowWallSigns,
        ShowOverheadMap = ShowOverheadMap,
        BumpyWalls = BumpyWalls,
        JewelCaustics = JewelCaustics,
        ClassicDepthCue = ClassicDepthCue,
        RetroPixelation = RetroPixelation,
        MazeBuildInAnim = MazeBuildInAnim,
        MazeRegenerate = MazeRegenerate,
        MazeOutroAnim = MazeOutroAnim,
        RecordMovie = RecordMovie,
        MovieStartSeconds = MovieStartSeconds,
        MovieDurationSeconds = MovieDurationSeconds,
        MovieFps = MovieFps,
        MovieQuality = MovieQuality,
        MovieBakeMs = MovieBakeMs,
        MovieLengthMode = MovieLengthMode,
        MovieWalks = MovieWalks,
        MovieShowFps = MovieShowFps,
        MovieDenoise = MovieDenoise,
    };
}

/// <summary>
/// The startup configuration screen — a single dialog that picks the renderer
/// backend (GPU / CPU) and every option for the chosen backend, then <b>Start</b>
/// runs it. The GPU group mirrors the productized Phase 6 knobs; the CPU group
/// exposes <see cref="RenderPreset"/> presets plus an advanced "Custom" panel (the
/// old CalibrationForm settings, minus the timed benchmark). Choices are read back
/// into <see cref="Result"/>; when CPU is selected, <see cref="SelectedCpuPreset"/>
/// holds the resolved preset. The screensaver <c>/c</c> switch opens this dialog in
/// <c>gpuOnly</c> mode (backend selector + CPU group hidden).
/// </summary>
internal sealed class ConfigForm : Form
{
    // ── GPU option tables ──
    private static readonly (string Label, RenderStyle Style)[] Styles =
    [
        ("Enhanced (spectral lighting)", RenderStyle.Enhanced),
        ("Classic (unlit, like the original)", RenderStyle.Classic),
    ];

    private static readonly (string Label, int W, int H)[] GpuResolutions =
    [
        ("1280 × 720", 1280, 720),
        ("1600 × 900", 1600, 900),
        ("1920 × 1080", 1920, 1080),
        ("2560 × 1440", 2560, 1440),
    ];

    private static readonly (string Label, SmokeMode Mode)[] SmokeOptions =
    [
        ("No smoke", SmokeMode.None),
        ("Biome (mixed)", SmokeMode.Biome),
        ("Fog", SmokeMode.AlwaysFog),
        ("Ground smoke", SmokeMode.AlwaysGroundSmoke),
    ];

    private static readonly Phase5DebugMode[] Views =
        (Phase5DebugMode[])Enum.GetValues(typeof(Phase5DebugMode));

    // ── GPU advanced-panel tables ──
    private static readonly uint[] GpuMaxSampleOptions = [128, 256, 512, 1024, 2048, 4096];
    private static readonly uint[] GpuMotionCapOptions = [8, 12, 16, 24, 32];

    // ── Movie recording tables ──
    private static readonly (string label, MovieQualityMode mode)[] MovieQualityOptions =
        [("Real-time (fast)", MovieQualityMode.Realtime), ("Beautiful (bake)", MovieQualityMode.Beautiful)];
    private static readonly (string label, MovieLengthMode mode)[] MovieLengthOptions =
        [("Record seconds", MovieLengthMode.Duration), ("Complete walks", MovieLengthMode.Walks)];

    // ── CPU option tables (ported from CalibrationForm) ──
    private static readonly RenderPreset[] CpuPresets = RenderPreset.QualityPresets;

    private static readonly (string label, int w, int h)[] CpuResolutions =
        [("320 x 240", 320, 240), ("480 x 360", 480, 360),
         ("640 x 480", 640, 480), ("800 x 600", 800, 600)];

    private static readonly (string label, float value)[] Scales =
        [("0.75x", 0.75f), ("1.0x", 1.0f), ("1.25x", 1.25f),
         ("1.5x", 1.5f), ("2.0x", 2.0f)];

    private static readonly int[] SppOptions = [1, 2, 4, 8, 16];
    private static readonly uint[] MaxSampleOptions = [50, 100, 200, 300, 500, 800, 1000];
    private static readonly uint[] MotionCapOptions = [4, 8, 16, 24, 32, 48];

    private static readonly (string label, int radius)[] FilterOptions =
        [("Off", 0), ("3x3", 1), ("5x5", 2)];

    private static readonly int[] TileSizeOptions = [16, 32, 64];

    private static readonly (string label, LightingMode mode)[] LightingOptions =
        [("None", LightingMode.None), ("Direct", LightingMode.Direct), ("NEE", LightingMode.NEE)];

    private static readonly (string label, SmokeMode mode)[] SmokeModeOptions =
        [("Off", SmokeMode.None), ("Biome", SmokeMode.Biome), ("Always fog", SmokeMode.AlwaysFog), ("Always ground smoke", SmokeMode.AlwaysGroundSmoke)];

    private static readonly (string label, VolumetricQuality quality)[] VolumetricQualityOptions =
        [("Off", VolumetricQuality.Off), ("Low", VolumetricQuality.Low), ("Medium", VolumetricQuality.Medium), ("High", VolumetricQuality.High), ("Ultra", VolumetricQuality.Ultra)];

    private static readonly (string label, CpuThrottle value)[] ThrottleOptions =
    [
        ("Normal (all cores)",                                      CpuThrottle.Normal),
        ("Below normal priority",                                   CpuThrottle.BelowNormal),
        ($"Minus 1 ({Math.Max(1, Environment.ProcessorCount - 1)} cores)", CpuThrottle.MinusOne),
        ($"Half ({Math.Max(1, Environment.ProcessorCount / 2)} cores)",    CpuThrottle.Half),
        ("1 core",                                                  CpuThrottle.One)
    ];

    /// <summary>The read-back settings (GPU knobs + backend + CPU selection).</summary>
    public AppSettings Result { get; }

    /// <summary>Selected backend (valid after the dialog returns OK).</summary>
    public RenderBackend Backend => Result.Backend;

    /// <summary>Resolved CPU preset (valid after OK when <see cref="Backend"/> is CPU).</summary>
    public RenderPreset SelectedCpuPreset { get; private set; } = RenderPreset.Medium;

    // ── Backend selector ──
    private readonly RadioButton _backendGpu;
    private readonly RadioButton _backendCpu;

    // ── GPU controls ──
    private readonly Panel _gpuPanel;
    private readonly ComboBox _style;
    private readonly ComboBox _resolution;
    private readonly CheckBox _fullscreen;
    private readonly ComboBox _smoke;
    private readonly ComboBox _fogQuality;
    private readonly ComboBox _startView;
    private readonly NumericUpDown _mazeSize;
    private readonly NumericUpDown _fogDrift;
    private readonly NumericUpDown _clamp;
    private readonly CheckBox _dayNight;
    private readonly NumericUpDown _dayLength;
    private readonly NumericUpDown _stillTime;
    private readonly Label _smokeLabel;
    private readonly Label _fogQualityLabel;
    private readonly Label _fogLabel;
    private readonly Label _clampLabel;
    private readonly Label _dayNightLabel;
    private readonly Label _dayLengthLabel;
    // GPU advanced panel (convergence / denoise)
    private readonly GroupBox _gpuAdvanced;
    private readonly ComboBox _gpuMaxSamples;
    private readonly ComboBox _gpuMotionCap;
    private readonly ComboBox _gpuFilter;
    private readonly CheckBox _gpuJitter;
    private readonly NumericUpDown _gpuTemporalBlend;
    private readonly CheckBox _showRat;
    private readonly CheckBox _showLogo;
    private readonly CheckBox _showSigns;
    private readonly CheckBox _showMap;
    private readonly CheckBox _bumpy;
    private readonly CheckBox _buildIn;
    private readonly CheckBox _regen;
    private readonly CheckBox _outro;
    private readonly CheckBox _depthCue;
    private readonly CheckBox _retro;
    private readonly CheckBox _jewelCaustics;
    private readonly CheckBox _outdoor;
    // Movie recording group
    private readonly GroupBox _movieBox;
    private readonly CheckBox _recordMovie;
    private readonly ComboBox _movieQuality;
    private readonly NumericUpDown _movieBakeMs;
    private readonly ComboBox _movieLength;
    private readonly NumericUpDown _movieStart;
    private readonly NumericUpDown _movieDuration;
    private readonly NumericUpDown _movieWalks;
    private readonly NumericUpDown _movieFps;
    private readonly CheckBox _movieShowFps;
    private readonly CheckBox _movieDenoise;
    private readonly PictureBox _moviePreview;
    private readonly Label _moviePreviewHint;

    // ── CPU controls ──
    private readonly Panel _cpuPanel;
    private readonly ComboBox _cpuPreset;
    private readonly ComboBox _cpuThrottle;
    private readonly GroupBox _cpuAdvanced;
    private readonly ComboBox _cboResolution;
    private readonly ComboBox _cboScale;
    private readonly ComboBox _cboSpp;
    private readonly ComboBox _cboMaxSamples;
    private readonly ComboBox _cboMotionCap;
    private readonly ComboBox _cboFilter;
    private readonly ComboBox _cboTileSize;
    private readonly ComboBox _cboLighting;
    private readonly ComboBox _cboSmoke;
    private readonly ComboBox _cboVolumetric;
    private readonly CheckBox _chkJitter;
    private readonly CheckBox _chkEdgeAware;
    private readonly CheckBox _chkChecker;
    private readonly CheckBox _chkEma;
    private readonly CheckBox _chkClamp;

    public ConfigForm(AppSettings settings, bool gpuOnly = false)
    {
        Result = settings.Clone();

        Text = "Spectral Maze — ray tracer";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 832);

        var title = new Label
        {
            Text = "Spectral Maze",
            Left = 16, Top = 14, Width = 428, Height = 26,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
        };
        var subtitle = new Label
        {
            Text = "Choose a renderer and your settings, then Start.",
            Left = 16, Top = 42, Width = 428, Height = 20,
            ForeColor = SystemColors.GrayText,
        };

        // ── Backend selector ──
        var backendLabel = new Label { Text = "Renderer", Left = 16, Top = 79, Width = 150, Height = 22 };
        _backendGpu = new RadioButton { Left = 172, Top = 77, Width = 130, Text = "GPU (DXR)", Checked = Result.Backend == RenderBackend.Gpu };
        _backendCpu = new RadioButton { Left = 306, Top = 77, Width = 150, Text = "CPU (software)", Checked = Result.Backend == RenderBackend.Cpu };

        // ── GPU panel ── the knob rows + the props group box + the Advanced group box. The content is
        // taller than the visible height, so AutoScroll adds a scrollbar for the overflow (the Advanced
        // box) rather than forcing an over-tall dialog on smaller screens.
        _gpuPanel = new Panel { Left = 4, Top = 104, Width = 452, Height = 680, AutoScroll = true };
        {
            int y = 0;
            const int rowH = 34;
            Label Row(string text)
            {
                var l = new Label { Text = text, Left = 12, Top = y + 3, Width = 150, Height = 22 };
                return l;
            }

            _style = new ComboBox { Left = 168, Top = y, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var s in Styles) _style.Items.Add(s.Label);
            _style.SelectedIndex = Math.Max(0, Array.FindIndex(Styles, s => s.Style == Result.Style));
            Label styleLabel = Row("Look");
            y += rowH;

            _resolution = new ComboBox { Left = 168, Top = y, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var r in GpuResolutions) _resolution.Items.Add(r.Label);
            _resolution.SelectedIndex = Math.Max(0, Array.FindIndex(GpuResolutions,
                r => r.W == Result.Width && r.H == Result.Height));
            Label resLabel = Row("Resolution");
            y += rowH;

            _fullscreen = new CheckBox { Left = 168, Top = y, Width = 232, Text = "Fullscreen (borderless)", Checked = Result.Fullscreen };
            Label fsLabel = Row("Display");
            y += rowH;

            _smoke = new ComboBox { Left = 168, Top = y, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var s in SmokeOptions) _smoke.Items.Add(s.Label);
            _smoke.SelectedIndex = Math.Max(0, Array.FindIndex(SmokeOptions, s => s.Mode == Result.SmokeMode));
            _smokeLabel = Row("Smoke");
            y += rowH;

            _fogQuality = new ComboBox { Left = 168, Top = y, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var q in VolumetricQualityOptions) _fogQuality.Items.Add(q.label);
            _fogQuality.SelectedIndex = Math.Max(0, Array.FindIndex(VolumetricQualityOptions, q => q.quality == Result.VolumetricQuality));
            _fogQualityLabel = Row("Fog quality");
            y += rowH;

            _startView = new ComboBox { Left = 168, Top = y, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (Phase5DebugMode v in Views) _startView.Items.Add(v);
            _startView.SelectedItem = Result.StartView;
            Label viewLabel = Row("Start view");
            y += rowH;

            _mazeSize = new NumericUpDown { Left = 168, Top = y, Width = 232, Minimum = 8, Maximum = 48, Value = Math.Clamp(Result.MazeSize, 8, 48) };
            Label mazeLabel = Row("Maze size");
            y += rowH;

            _fogDrift = new NumericUpDown { Left = 168, Top = y, Width = 232, Minimum = 0m, Maximum = 5m, DecimalPlaces = 1, Increment = 0.1m, Value = (decimal)Math.Clamp(Result.FogDrift, 0f, 5f) };
            _fogLabel = Row("Fog drift speed");
            y += rowH;

            _clamp = new NumericUpDown { Left = 168, Top = y, Width = 232, Minimum = 0m, Maximum = 20m, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)Math.Clamp(Result.SampleClamp, 0f, 20f) };
            _clampLabel = Row("Firefly clamp");
            y += rowH;

            _outdoor = new CheckBox { Left = 168, Top = y, Width = 232, Text = "Outdoor garden half (open sky)", Checked = Result.OutdoorArea };
            Label outdoorLabel = Row("Scene");
            y += rowH;

            _dayNight = new CheckBox { Left = 168, Top = y, Width = 232, Text = "Cycle sun over time", Checked = Result.DayNightCycle };
            _dayNightLabel = Row("Day / night");
            y += rowH;

            _dayLength = new NumericUpDown { Left = 168, Top = y, Width = 232, Minimum = 10m, Maximum = 3600m, Increment = 10m, Value = (decimal)Math.Clamp(Result.DayLengthSeconds, 10f, 3600f) };
            _dayLengthLabel = Row("Day length (s)");
            y += rowH;

            _stillTime = new NumericUpDown { Left = 168, Top = y, Width = 232, Minimum = 0m, Maximum = 10m, DecimalPlaces = 1, Increment = 0.1m, Value = (decimal)Math.Clamp(Result.StillTime, 0f, 10f) };
            Label stillLabel = Row("Camera dwell (s)");
            y += rowH + 6;

            var propsBox = new GroupBox { Left = 12, Top = y, Width = 388, Height = 184, Text = "Classic props & animations" };
            _showRat = new CheckBox { Left = 12, Top = 22, Width = 176, Text = "Rat", Checked = Result.ShowRat };
            _showLogo = new CheckBox { Left = 12, Top = 48, Width = 176, Text = "OpenGL logo", Checked = Result.ShowOpenGlLogo };
            _showSigns = new CheckBox { Left = 12, Top = 74, Width = 176, Text = "Wall signs", Checked = Result.ShowWallSigns };
            _showMap = new CheckBox { Left = 12, Top = 100, Width = 176, Text = "Overhead map", Checked = Result.ShowOverheadMap };
            _depthCue = new CheckBox { Left = 12, Top = 126, Width = 176, Text = "Classic depth cue", Checked = Result.ClassicDepthCue };
            _bumpy = new CheckBox { Left = 198, Top = 22, Width = 182, Text = "Bumpy walls", Checked = Result.BumpyWalls };
            _buildIn = new CheckBox { Left = 198, Top = 48, Width = 182, Text = "Wall build-in intro", Checked = Result.MazeBuildInAnim };
            _regen = new CheckBox { Left = 198, Top = 74, Width = 182, Text = "Regenerate on finish", Checked = Result.MazeRegenerate };
            _outro = new CheckBox { Left = 198, Top = 100, Width = 182, Text = "Completion animation", Checked = Result.MazeOutroAnim };
            _retro = new CheckBox { Left = 198, Top = 126, Width = 182, Text = "Retro pixelation", Checked = Result.RetroPixelation };
            _jewelCaustics = new CheckBox { Left = 12, Top = 152, Width = 368, Text = "Jewel caustics (adds a startup/regeneration hitch)", Checked = Result.JewelCaustics };
            propsBox.Controls.AddRange([_showRat, _showLogo, _showSigns, _showMap, _depthCue, _bumpy, _buildIn, _regen, _outro, _retro, _jewelCaustics]);

            // ── Advanced (GPU convergence & denoise) ── the Phase6Renderer knobs that were hardcoded.
            _gpuAdvanced = new GroupBox { Left = 12, Top = propsBox.Bottom + 8, Width = 388, Height = 122, Text = "Advanced (convergence & denoise)" };
            static Label ALbl(string t, int x, int yy) => new() { Text = t, Left = x, Top = yy, AutoSize = true };
            _gpuMaxSamples = new ComboBox { Left = 104, Top = 22, Width = 92, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (uint v in GpuMaxSampleOptions) _gpuMaxSamples.Items.Add(v.ToString());
            _gpuMaxSamples.SelectedIndex = Math.Max(0, Array.FindIndex(GpuMaxSampleOptions, v => v == Result.MaxSampleCount));
            _gpuMotionCap = new ComboBox { Left = 300, Top = 22, Width = 76, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (uint v in GpuMotionCapOptions) _gpuMotionCap.Items.Add(v.ToString());
            _gpuMotionCap.SelectedIndex = Math.Max(0, Array.FindIndex(GpuMotionCapOptions, v => v == Result.MotionSampleCap));
            _gpuFilter = new ComboBox { Left = 104, Top = 54, Width = 92, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var f in FilterOptions) _gpuFilter.Items.Add(f.label);
            _gpuFilter.SelectedIndex = Math.Max(0, Array.FindIndex(FilterOptions, f => f.radius == Result.FilterRadius));
            _gpuTemporalBlend = new NumericUpDown { Left = 300, Top = 53, Width = 76, Minimum = 0m, Maximum = 1m, DecimalPlaces = 2, Increment = 0.05m, Value = (decimal)Math.Clamp(Result.TemporalBlendAlpha, 0f, 1f) };
            _gpuJitter = new CheckBox { Left = 12, Top = 88, Width = 360, Text = "Sub-pixel jitter (anti-aliasing)", Checked = Result.SubPixelJitter };
            _gpuAdvanced.Controls.AddRange([
                ALbl("Max samples", 12, 25), _gpuMaxSamples,
                ALbl("Motion cap", 210, 25), _gpuMotionCap,
                ALbl("Filter", 12, 57), _gpuFilter,
                ALbl("Blend α", 210, 57), _gpuTemporalBlend,
                _gpuJitter,
            ]);

            // ── Record movie (headless walk → MP4 via ffmpeg) ──
            _movieBox = new GroupBox { Left = 12, Top = _gpuAdvanced.Bottom + 8, Width = 388, Height = 372, Text = "Record movie (ffmpeg)" };
            _recordMovie = new CheckBox { Left = 12, Top = 22, Width = 360, Text = "Record the walk to a video instead of live view", Checked = Result.RecordMovie };

            _movieQuality = new ComboBox { Left = 104, Top = 50, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var q in MovieQualityOptions) _movieQuality.Items.Add(q.label);
            _movieQuality.SelectedIndex = Math.Max(0, Array.FindIndex(MovieQualityOptions, q => q.mode == Result.MovieQuality));
            _movieShowFps = new CheckBox { Left = 250, Top = 52, Width = 126, Text = "Burn in FPS", Checked = Result.MovieShowFps };
            _movieDenoise = new CheckBox { Left = 244, Top = 114, Width = 140, Text = "Denoise (Beautiful)", Checked = Result.MovieDenoise };

            _movieBakeMs = new NumericUpDown { Left = 104, Top = 82, Width = 90, Minimum = 10m, Maximum = 60000m, DecimalPlaces = 0, Increment = 50m, Value = (decimal)Math.Clamp(Result.MovieBakeMs, 10f, 60000f) };
            _movieFps = new NumericUpDown { Left = 300, Top = 82, Width = 76, Minimum = 5m, Maximum = 60m, DecimalPlaces = 0, Increment = 5m, Value = Math.Clamp(Result.MovieFps, 5, 60) };

            _movieLength = new ComboBox { Left = 104, Top = 112, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var l in MovieLengthOptions) _movieLength.Items.Add(l.label);
            _movieLength.SelectedIndex = Math.Max(0, Array.FindIndex(MovieLengthOptions, l => l.mode == Result.MovieLengthMode));

            _movieStart = new NumericUpDown { Left = 104, Top = 142, Width = 76, Minimum = 0m, Maximum = 3600m, DecimalPlaces = 0, Increment = 1m, Value = (decimal)Math.Clamp(Result.MovieStartSeconds, 0f, 3600f) };
            _movieDuration = new NumericUpDown { Left = 300, Top = 142, Width = 76, Minimum = 1m, Maximum = 3600m, DecimalPlaces = 0, Increment = 5m, Value = (decimal)Math.Clamp(Result.MovieDurationSeconds, 1f, 3600f) };
            _movieWalks = new NumericUpDown { Left = 104, Top = 172, Width = 76, Minimum = 1m, Maximum = 100m, DecimalPlaces = 0, Increment = 1m, Value = Math.Clamp(Result.MovieWalks, 1, 100) };

            // Recent-frame preview (a slightly-stale thumbnail cached by the live app / last recording), so
            // the user can see what the walk currently looks like without launching it.
            _moviePreview = new PictureBox { Left = 104, Top = 204, Width = 224, Height = 126, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
            _moviePreviewHint = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gainsboro, Text = "No preview yet —\nrun the app once" };
            _moviePreview.Controls.Add(_moviePreviewHint);

            _movieBox.Controls.AddRange([
                _recordMovie,
                ALbl("Quality", 12, 53), _movieQuality, _movieShowFps,
                ALbl("Budget (ms)", 12, 85), _movieBakeMs, ALbl("Frame rate", 210, 85), _movieFps,
                ALbl("Length", 12, 115), _movieLength, _movieDenoise,
                ALbl("Start after (s)", 12, 145), _movieStart, ALbl("Duration (s)", 210, 145), _movieDuration,
                ALbl("Walks", 12, 175), _movieWalks,
                ALbl("Recent frame", 12, 210), _moviePreview,
                new Label { Text = "Saved to Videos\\SpectralMaze (temp frames auto-deleted).", Left = 12, Top = 340, Width = 372, Height = 20, ForeColor = SystemColors.GrayText },
            ]);
            LoadMoviePreview();

            _gpuPanel.Controls.AddRange([
                styleLabel, _style,
                resLabel, _resolution,
                fsLabel, _fullscreen,
                _smokeLabel, _smoke,
                _fogQualityLabel, _fogQuality,
                viewLabel, _startView,
                mazeLabel, _mazeSize,
                _fogLabel, _fogDrift,
                _clampLabel, _clamp,
                outdoorLabel, _outdoor,
                _dayNightLabel, _dayNight,
                _dayLengthLabel, _dayLength,
                stillLabel, _stillTime,
                propsBox,
                _gpuAdvanced,
                _movieBox,
            ]);

            _style.SelectedIndexChanged += (_, _) => SyncGpuEnabled();
            _outdoor.CheckedChanged += (_, _) => SyncGpuEnabled();
            _dayNight.CheckedChanged += (_, _) => SyncGpuEnabled();
            _recordMovie.CheckedChanged += (_, _) => SyncGpuEnabled();
            // Switching quality nudges the per-frame budget to a sensible preset (fast vs. converged); the
            // user can still fine-tune it afterward.
            _movieQuality.SelectedIndexChanged += (_, _) =>
            {
                _movieBakeMs.Value = MovieQualityOptions[Math.Max(0, _movieQuality.SelectedIndex)].mode == MovieQualityMode.Realtime ? 150m : 400m;
                SyncGpuEnabled();
            };
            _movieLength.SelectedIndexChanged += (_, _) => SyncGpuEnabled();
            SyncGpuEnabled();
        }

        // ── CPU panel ──
        _cpuPanel = new Panel { Left = 4, Top = 104, Width = 452, Height = 410 };
        {
            _cpuPreset = new ComboBox { Left = 168, Top = 0, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var p in CpuPresets) _cpuPreset.Items.Add($"{p.Name}  ({p.EffectiveWidth}×{p.EffectiveHeight}, {p.SppPerJob} spp)");
            _cpuPreset.Items.Add("Custom (advanced settings)");
            var presetLabel = new Label { Text = "Quality preset", Left = 12, Top = 3, Width = 150, Height = 22 };

            _cpuThrottle = new ComboBox { Left = 168, Top = 34, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var t in ThrottleOptions) _cpuThrottle.Items.Add(t.label);
            _cpuThrottle.SelectedIndex = Math.Max(0, Array.FindIndex(ThrottleOptions, t => t.value == Result.CpuThrottle));
            var throttleLabel = new Label { Text = "CPU usage", Left = 12, Top = 37, Width = 150, Height = 22 };

            _cpuAdvanced = new GroupBox { Left = 12, Top = 74, Width = 428, Height = 232, Text = "Custom settings" };

            Label Lbl(string t, int x, int y) => new() { Text = t, Left = x, Top = y, AutoSize = true };
            ComboBox Cbo(int x, int y, int w, IEnumerable<string> items)
            {
                var c = new ComboBox { Left = x, Top = y, Width = w, DropDownStyle = ComboBoxStyle.DropDownList };
                c.Items.AddRange(items.ToArray());
                if (c.Items.Count > 0) c.SelectedIndex = 0;
                return c;
            }

            _cpuAdvanced.Controls.Add(Lbl("Resolution", 12, 25));
            _cboResolution = Cbo(110, 22, 104, CpuResolutions.Select(r => r.label));
            _cpuAdvanced.Controls.Add(_cboResolution);
            _cpuAdvanced.Controls.Add(Lbl("Scale", 232, 25));
            _cboScale = Cbo(320, 22, 96, Scales.Select(s => s.label));
            _cpuAdvanced.Controls.Add(_cboScale);

            _cpuAdvanced.Controls.Add(Lbl("Samples/pass", 12, 55));
            _cboSpp = Cbo(110, 52, 104, SppOptions.Select(v => v.ToString()));
            _cpuAdvanced.Controls.Add(_cboSpp);
            _cpuAdvanced.Controls.Add(Lbl("Max samples", 232, 55));
            _cboMaxSamples = Cbo(320, 52, 96, MaxSampleOptions.Select(v => v.ToString()));
            _cpuAdvanced.Controls.Add(_cboMaxSamples);

            _cpuAdvanced.Controls.Add(Lbl("Motion cap", 12, 85));
            _cboMotionCap = Cbo(110, 82, 104, MotionCapOptions.Select(v => v.ToString()));
            _cpuAdvanced.Controls.Add(_cboMotionCap);
            _cpuAdvanced.Controls.Add(Lbl("Filter", 232, 85));
            _cboFilter = Cbo(320, 82, 96, FilterOptions.Select(f => f.label));
            _cpuAdvanced.Controls.Add(_cboFilter);

            _cpuAdvanced.Controls.Add(Lbl("Tile size", 12, 115));
            _cboTileSize = Cbo(110, 112, 104, TileSizeOptions.Select(v => v.ToString()));
            _cpuAdvanced.Controls.Add(_cboTileSize);
            _cpuAdvanced.Controls.Add(Lbl("Lighting", 232, 115));
            _cboLighting = Cbo(320, 112, 96, LightingOptions.Select(l => l.label));
            _cpuAdvanced.Controls.Add(_cboLighting);

            _cpuAdvanced.Controls.Add(Lbl("Smoke", 12, 145));
            _cboSmoke = Cbo(110, 142, 104, SmokeModeOptions.Select(s => s.label));
            _cpuAdvanced.Controls.Add(_cboSmoke);
            _cpuAdvanced.Controls.Add(Lbl("Volume", 232, 145));
            _cboVolumetric = Cbo(320, 142, 96, VolumetricQualityOptions.Select(v => v.label));
            _cpuAdvanced.Controls.Add(_cboVolumetric);

            _chkJitter = new CheckBox { Text = "Sub-pixel jitter", Left = 12, Top = 172, Width = 130 };
            _chkEdgeAware = new CheckBox { Text = "Edge-aware filter", Left = 150, Top = 172, Width = 140 };
            _cpuAdvanced.Controls.Add(_chkJitter);
            _cpuAdvanced.Controls.Add(_chkEdgeAware);

            _chkChecker = new CheckBox { Text = "Checker motion", Left = 12, Top = 198, Width = 130 };
            _chkEma = new CheckBox { Text = "Temporal EMA", Left = 150, Top = 198, Width = 130 };
            _chkClamp = new CheckBox { Text = "Sample clamp", Left = 290, Top = 198, Width = 130 };
            _cpuAdvanced.Controls.Add(_chkChecker);
            _cpuAdvanced.Controls.Add(_chkEma);
            _cpuAdvanced.Controls.Add(_chkClamp);

            _cpuPanel.Controls.AddRange([presetLabel, _cpuPreset, throttleLabel, _cpuThrottle, _cpuAdvanced]);

            // Seed the preset dropdown + advanced controls from persisted settings.
            int presetIdx = string.Equals(Result.CpuPreset, "Custom", StringComparison.OrdinalIgnoreCase)
                ? CpuPresets.Length
                : Math.Max(0, Array.FindIndex(CpuPresets, p => p.Name == Result.CpuPreset));
            if (presetIdx == CpuPresets.Length)
                PopulateCpuAdvancedFrom(CustomToPreset(Result.CpuCustom));
            _cpuPreset.SelectedIndex = presetIdx;
            _cpuPreset.SelectedIndexChanged += (_, _) => SyncCpuAdvanced();
            SyncCpuAdvanced();
        }

        // ── Buttons ── below the GPU panel (its bottom is 104 + 680 = 784).
        var presetPickLabel = new Label { Text = "Preset", Left = 16, Top = 799, Width = 44, Height = 20 };
        var presetCombo = new ComboBox { Left = 60, Top = 795, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var p in Presets) presetCombo.Items.Add(p.Label);
        presetCombo.SelectedIndex = 0; // "Load a preset…" prompt
        presetCombo.SelectedIndexChanged += (_, _) =>
        {
            int i = presetCombo.SelectedIndex;
            if (i <= 0) return;              // the prompt row
            ApplyPreset(Presets[i]);
            presetCombo.SelectedIndex = 0;   // back to the prompt (re-fires with i==0 → no-op)
        };
        var start = new Button { Text = "Start", Left = 274, Top = 794, Width = 82, Height = 28, DialogResult = DialogResult.OK };
        var exit = new Button { Text = "Exit", Left = 362, Top = 794, Width = 82, Height = 28, DialogResult = DialogResult.Cancel };
        start.Click += (_, _) => ApplyResult();

        Controls.AddRange([title, subtitle, backendLabel, _backendGpu, _backendCpu, _gpuPanel, _cpuPanel, presetPickLabel, presetCombo, start, exit]);
        AcceptButton = start;
        CancelButton = exit;
        FormClosed += (_, _) => _moviePreview.Image?.Dispose(); // the loaded thumbnail isn't auto-disposed

        _backendGpu.CheckedChanged += (_, _) => SyncBackend();
        _backendCpu.CheckedChanged += (_, _) => SyncBackend();

        if (gpuOnly)
        {
            // Screensaver /c: always GPU. Hide the backend selector + CPU group.
            _backendGpu.Checked = true;
            backendLabel.Visible = _backendGpu.Visible = _backendCpu.Visible = false;
        }
        SyncBackend();
    }

    private void SyncBackend()
    {
        bool gpu = _backendGpu.Checked;
        _gpuPanel.Visible = gpu;
        _cpuPanel.Visible = !gpu;
    }

    // Classic is unlit and smoke-free, so the smoke / fog / firefly knobs don't apply — grey them out when
    // Classic is selected; movie knobs are enabled only while recording, and each sub-mode greys out the
    // fields the other mode owns. Called from the option-change handlers and the max-quality preset.
    private void SyncGpuEnabled()
    {
        bool classic = Styles[_style.SelectedIndex].Style == RenderStyle.Classic;
        foreach (Control c in new Control[] { _smoke, _smokeLabel, _fogQuality, _fogQualityLabel, _fogDrift, _fogLabel, _clamp, _clampLabel })
            c.Enabled = !classic;
        _depthCue.Enabled = classic; // §1.4 depth cue only applies to the unlit Classic look
        _retro.Enabled = classic;    // §1.5 retro pixelation is a Classic authenticity toggle
        _jewelCaustics.Enabled = !classic; // §B4 caustics need lighting — not in the unlit Classic look
        _outdoor.Enabled = !classic; // open sky belongs to the spectral Enhanced look, not retro Classic
        // Day/night rides the open-air Enhanced garden; disable it in Classic or when Outdoor is off,
        // and the day-length only matters when the cycle itself is on.
        bool dayNightAvail = !classic && _outdoor.Checked;
        _dayNight.Enabled = _dayNightLabel.Enabled = dayNightAvail;
        _dayLength.Enabled = _dayLengthLabel.Enabled = dayNightAvail && _dayNight.Checked;
        bool rec = _recordMovie.Checked;
        bool walks = rec && MovieLengthOptions[Math.Max(0, _movieLength.SelectedIndex)].mode == MovieLengthMode.Walks;
        bool beautiful = rec && MovieQualityOptions[Math.Max(0, _movieQuality.SelectedIndex)].mode == MovieQualityMode.Beautiful;
        _movieQuality.Enabled = _movieLength.Enabled = _movieFps.Enabled = _movieShowFps.Enabled = rec;
        _movieBakeMs.Enabled = rec;                            // per-frame budget applies to both modes
        _movieDenoise.Enabled = beautiful;                     // denoise only cleans the static Beautiful bake
        _movieStart.Enabled = _movieDuration.Enabled = rec && !walks; // start/duration only in Duration length
        _movieWalks.Enabled = walks;                            // walk count only in Walks length
    }

    // Loads the cached render thumbnail into the Record-movie box, or shows the "no preview" hint. Reads
    // via a copied byte[] so the file stays unlocked (the running app keeps rewriting it).
    private void LoadMoviePreview()
    {
        try
        {
            string path = AppSettings.PreviewImagePath;
            if (File.Exists(path))
            {
                byte[] bytes = File.ReadAllBytes(path);
                using var ms = new MemoryStream(bytes);
                using var img = Image.FromStream(ms);
                _moviePreview.Image?.Dispose();
                _moviePreview.Image = new Bitmap(img);
                _moviePreviewHint.Visible = false;
                return;
            }
        }
        catch { /* corrupt / mid-write preview → fall back to the hint */ }
        _moviePreviewHint.Visible = true;
    }

    /// <summary>A one-click GPU/movie preset for the dialog's Preset dropdown. Sets quality + features;
    /// <see cref="ApplyPreset"/> deliberately leaves content/timing choices (maze size, camera dwell, day
    /// length, movie start/duration) alone. <c>Record</c> off = live view; on = a Beautiful bake at
    /// <c>BakeMs</c> per frame. The first row is the dropdown prompt and is never applied.</summary>
    private sealed record Preset(string Label, int Width, int Height, VolumetricQuality Fog,
        uint MaxSamples, uint MotionCap, bool Caustics, bool Record, float BakeMs);

    private static readonly Preset[] Presets =
    [
        new("Load a preset…",             0,    0, VolumetricQuality.Medium,    0,  0, false, false,    0f),
        // Live view (no recording), lightest → richest. "low": max framerate. "running": normal smooth
        // interaction. "demo": all eye-candy on, still real-time.
        new("Low — fastest live",      1280,  720, VolumetricQuality.Low,     512u,  8u, false, false,   0f),
        new("Real-time — running",     1280,  720, VolumetricQuality.Medium, 1024u, 12u, false, false,   0f),
        new("Real-time — demo",        1920, 1080, VolumetricQuality.High,   2048u, 16u, true,  false,   0f),
        // Recorded Beautiful bakes. BakeMs is a per-frame CAP now — each frame bakes only until it stops
        // changing (converged), so easy views finish in a fraction of the cap and only the hard ones (glass,
        // caustics, oil-slick tiles) spend it all. Raising the cap therefore buys clean heavy frames without
        // slowing the easy ones. "quick": 1080p High, cap 5 s. "balanced": 1440p High, cap 15 s.
        // "insane": 1440p Ultra, cap 30 s — lets the worst views fully converge (hours for a long walk).
        new("Pretty walk — quick",     1920, 1080, VolumetricQuality.High,   4096u, 32u, true,  true,  5000f),
        new("Pretty walk — balanced",  2560, 1440, VolumetricQuality.High,   4096u, 32u, true,  true, 15000f),
        new("Pretty walk — insane",    2560, 1440, VolumetricQuality.Ultra,  4096u, 32u, true,  true, 30000f),
    ];

    // Applies a dropdown preset to the GPU + movie controls (leaving scene/timing choices untouched).
    private void ApplyPreset(Preset p)
    {
        _backendGpu.Checked = true; // presets are GPU
        _style.SelectedIndex = Math.Max(0, Array.FindIndex(Styles, s => s.Style == RenderStyle.Enhanced));
        _startView.SelectedItem = Phase5DebugMode.Beauty;
        _smoke.SelectedIndex = Math.Max(0, Array.FindIndex(SmokeOptions, s => s.Mode == SmokeMode.Biome));
        _clamp.Value = 3m;
        _gpuFilter.SelectedIndex = Math.Max(0, Array.FindIndex(FilterOptions, f => f.radius == 1));
        _gpuJitter.Checked = true;
        _outdoor.Checked = true;
        _dayNight.Checked = true;
        // Props & animations on for every preset.
        _showRat.Checked = _showLogo.Checked = _showSigns.Checked = _showMap.Checked = true;
        _bumpy.Checked = true;
        _buildIn.Checked = _regen.Checked = _outro.Checked = true;

        _resolution.SelectedIndex = Math.Max(0, Array.FindIndex(GpuResolutions, r => r.W == p.Width && r.H == p.Height));
        _fogQuality.SelectedIndex = Math.Max(0, Array.FindIndex(VolumetricQualityOptions, o => o.quality == p.Fog));
        _gpuMaxSamples.SelectedIndex = Math.Max(0, Array.FindIndex(GpuMaxSampleOptions, v => v == p.MaxSamples));
        _gpuMotionCap.SelectedIndex = Math.Max(0, Array.FindIndex(GpuMotionCapOptions, v => v == p.MotionCap));
        _jewelCaustics.Checked = p.Caustics;

        _recordMovie.Checked = p.Record;
        if (p.Record) // a converged Beautiful bake
        {
            _movieQuality.SelectedIndex = Math.Max(0, Array.FindIndex(MovieQualityOptions, q => q.mode == MovieQualityMode.Beautiful));
            _movieBakeMs.Value = (decimal)p.BakeMs; // after quality — a quality change nudges this
            _movieFps.Value = 30m;
            _movieShowFps.Checked = false; // clean showcase frames
            _movieDenoise.Checked = true;  // clean up residual grain on the baked frame
        }

        SyncGpuEnabled();
    }

    private void SyncCpuAdvanced()
    {
        bool custom = _cpuPreset.SelectedIndex >= CpuPresets.Length;
        if (!custom && _cpuPreset.SelectedIndex >= 0)
            PopulateCpuAdvancedFrom(CpuPresets[_cpuPreset.SelectedIndex]);
        _cpuAdvanced.Enabled = custom;
    }

    private void ApplyResult()
    {
        Result.Backend = _backendCpu.Checked ? RenderBackend.Cpu : RenderBackend.Gpu;

        // GPU knobs are always captured (they back the screensaver + persist).
        Result.Style = Styles[_style.SelectedIndex].Style;
        (_, int w, int h) = GpuResolutions[Math.Max(0, _resolution.SelectedIndex)];
        Result.Width = w;
        Result.Height = h;
        Result.Fullscreen = _fullscreen.Checked;
        Result.OutdoorArea = _outdoor.Checked;
        Result.SmokeMode = SmokeOptions[Math.Max(0, _smoke.SelectedIndex)].Mode;
        Result.StartView = (Phase5DebugMode)_startView.SelectedItem!;
        Result.MazeSize = (int)_mazeSize.Value;
        Result.FogDrift = (float)_fogDrift.Value;
        Result.SampleClamp = (float)_clamp.Value;
        Result.VolumetricQuality = VolumetricQualityOptions[Math.Max(0, _fogQuality.SelectedIndex)].quality;
        Result.DayNightCycle = _dayNight.Checked;
        Result.DayLengthSeconds = (float)_dayLength.Value;
        Result.StillTime = (float)_stillTime.Value;
        Result.MaxSampleCount = GpuMaxSampleOptions[Math.Max(0, _gpuMaxSamples.SelectedIndex)];
        Result.MotionSampleCap = GpuMotionCapOptions[Math.Max(0, _gpuMotionCap.SelectedIndex)];
        Result.FilterRadius = FilterOptions[Math.Max(0, _gpuFilter.SelectedIndex)].radius;
        Result.SubPixelJitter = _gpuJitter.Checked;
        Result.TemporalBlendAlpha = (float)_gpuTemporalBlend.Value;
        Result.ShowRat = _showRat.Checked;
        Result.ShowOpenGlLogo = _showLogo.Checked;
        Result.ShowWallSigns = _showSigns.Checked;
        Result.ShowOverheadMap = _showMap.Checked;
        Result.BumpyWalls = _bumpy.Checked;
        Result.JewelCaustics = _jewelCaustics.Checked;
        Result.ClassicDepthCue = _depthCue.Checked;
        Result.RetroPixelation = _retro.Checked;
        Result.MazeBuildInAnim = _buildIn.Checked;
        Result.MazeRegenerate = _regen.Checked;
        Result.MazeOutroAnim = _outro.Checked;
        Result.RecordMovie = _recordMovie.Checked;
        Result.MovieQuality = MovieQualityOptions[Math.Max(0, _movieQuality.SelectedIndex)].mode;
        Result.MovieBakeMs = (float)_movieBakeMs.Value;
        Result.MovieLengthMode = MovieLengthOptions[Math.Max(0, _movieLength.SelectedIndex)].mode;
        Result.MovieStartSeconds = (float)_movieStart.Value;
        Result.MovieDurationSeconds = (float)_movieDuration.Value;
        Result.MovieWalks = (int)_movieWalks.Value;
        Result.MovieFps = (int)_movieFps.Value;
        Result.MovieShowFps = _movieShowFps.Checked;
        Result.MovieDenoise = _movieDenoise.Checked;

        // CPU selection.
        Result.CpuThrottle = ThrottleOptions[Safe(_cpuThrottle)].value;
        if (_cpuPreset.SelectedIndex >= CpuPresets.Length)
        {
            Result.CpuPreset = "Custom";
            CpuCustomSettings c = Result.CpuCustom;
            (_, c.RenderWidth, c.RenderHeight) = CpuResolutions[Safe(_cboResolution)];
            c.ResolutionScale = Scales[Safe(_cboScale)].value;
            c.SppPerJob = SppOptions[Safe(_cboSpp)];
            c.MaxSampleCount = MaxSampleOptions[Safe(_cboMaxSamples)];
            c.MotionSampleCap = MotionCapOptions[Safe(_cboMotionCap)];
            c.FilterRadius = FilterOptions[Safe(_cboFilter)].radius;
            c.TileSize = TileSizeOptions[Safe(_cboTileSize)];
            c.Lighting = LightingOptions[Safe(_cboLighting)].mode;
            c.SmokeMode = SmokeModeOptions[Safe(_cboSmoke)].mode;
            c.VolumetricQuality = VolumetricQualityOptions[Safe(_cboVolumetric)].quality;
            c.SubPixelJitter = _chkJitter.Checked;
            c.EdgeAwareFilter = _chkEdgeAware.Checked;
            c.CheckerboardMotion = _chkChecker.Checked;
            c.TemporalEma = _chkEma.Checked;
            c.SampleClamp = _chkClamp.Checked;
        }
        else
        {
            Result.CpuPreset = CpuPresets[Math.Max(0, _cpuPreset.SelectedIndex)].Name;
        }

        SelectedCpuPreset = BuildCpuPreset();
    }

    /// <summary>Resolves the current CPU control state into a <see cref="RenderPreset"/>.</summary>
    private RenderPreset BuildCpuPreset()
    {
        CpuThrottle throttle = ThrottleOptions[Safe(_cpuThrottle)].value;
        if (_cpuPreset.SelectedIndex >= 0 && _cpuPreset.SelectedIndex < CpuPresets.Length)
            return CpuPresets[_cpuPreset.SelectedIndex] with { ThrottleCpu = throttle };

        var (_, rw, rh) = CpuResolutions[Safe(_cboResolution)];
        float scale = Scales[Safe(_cboScale)].value;
        int spp = SppOptions[Safe(_cboSpp)];
        uint maxSamples = MaxSampleOptions[Safe(_cboMaxSamples)];
        uint motionCap = MotionCapOptions[Safe(_cboMotionCap)];
        int filterRadius = FilterOptions[Safe(_cboFilter)].radius;
        int tileSize = TileSizeOptions[Safe(_cboTileSize)];
        var lighting = LightingOptions[Safe(_cboLighting)].mode;
        var smokeMode = SmokeModeOptions[Safe(_cboSmoke)].mode;
        var volumetricQuality = VolumetricQualityOptions[Safe(_cboVolumetric)].quality;
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(volumetricQuality, smokeMode);

        return new RenderPreset("Custom", rw, rh, spp, maxSamples, motionCap,
            tileSize, _chkJitter.Checked, scale, filterRadius, _chkEdgeAware.Checked,
            lighting,
            CheckerboardMotion: _chkChecker.Checked,
            TemporalBlendAlpha: _chkEma.Checked ? 0.05f : 0f,
            SampleClamp: _chkClamp.Checked ? 10f : 0f,
            ThrottleCpu: throttle,
            SmokeMode: smokeMode,
            Volumetrics: volumetrics);
    }

    private static RenderPreset CustomToPreset(CpuCustomSettings c) => new(
        "Custom", c.RenderWidth, c.RenderHeight, c.SppPerJob, c.MaxSampleCount, c.MotionSampleCap,
        c.TileSize, c.SubPixelJitter, c.ResolutionScale, c.FilterRadius, c.EdgeAwareFilter,
        c.Lighting,
        CheckerboardMotion: c.CheckerboardMotion,
        TemporalBlendAlpha: c.TemporalEma ? 0.05f : 0f,
        SampleClamp: c.SampleClamp ? 10f : 0f,
        SmokeMode: c.SmokeMode,
        Volumetrics: VolumetricOptions.FromQuality(c.VolumetricQuality, c.SmokeMode));

    private void PopulateCpuAdvancedFrom(RenderPreset p)
    {
        _cboResolution.SelectedIndex = SafeFind(
            Array.FindIndex(CpuResolutions, r => r.w == p.RenderWidth && r.h == p.RenderHeight),
            CpuResolutions.Length);
        _cboScale.SelectedIndex = SafeFind(
            Array.FindIndex(Scales, s => MathF.Abs(s.value - p.ResolutionScale) < 0.01f),
            Scales.Length, 1);
        _cboSpp.SelectedIndex = SafeFind(
            Array.IndexOf(SppOptions, p.SppPerJob), SppOptions.Length, 2);
        _cboMaxSamples.SelectedIndex = SafeFind(
            Array.IndexOf(MaxSampleOptions, p.MaxSampleCount), MaxSampleOptions.Length, 4);
        _cboMotionCap.SelectedIndex = SafeFind(
            Array.IndexOf(MotionCapOptions, p.MotionSampleCap), MotionCapOptions.Length, 2);
        _cboFilter.SelectedIndex = SafeFind(
            Array.FindIndex(FilterOptions, f => f.radius == p.FilterRadius),
            FilterOptions.Length, 1);
        _cboTileSize.SelectedIndex = SafeFind(
            Array.IndexOf(TileSizeOptions, p.TileSize), TileSizeOptions.Length, 1);
        _cboLighting.SelectedIndex = SafeFind(
            Array.FindIndex(LightingOptions, l => l.mode == p.Lighting),
            LightingOptions.Length);
        _cboSmoke.SelectedIndex = SafeFind(
            Array.FindIndex(SmokeModeOptions, s => s.mode == p.SmokeMode),
            SmokeModeOptions.Length, 1);
        _cboVolumetric.SelectedIndex = SafeFind(
            Array.FindIndex(VolumetricQualityOptions, v => v.quality == p.Volumetrics.Quality),
            VolumetricQualityOptions.Length, 2);
        _chkJitter.Checked = p.SubPixelJitter;
        _chkEdgeAware.Checked = p.EdgeAwareFilter;
        _chkChecker.Checked = p.CheckerboardMotion;
        _chkEma.Checked = p.TemporalBlendAlpha > 0f;
        _chkClamp.Checked = p.SampleClamp > 0f;
    }

    // ── Tiny helpers ──
    private static int SafeFind(int index, int count, int fallback = 0)
        => index >= 0 && index < count ? index : fallback;

    private static int Safe(ComboBox cbo)
        => Math.Clamp(cbo.SelectedIndex, 0, cbo.Items.Count - 1);
}
