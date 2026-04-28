using RayTracer;

namespace MiniRay;

/// <summary>
/// Modal dialog that benchmarks the CPU for ~5 seconds at the lowest
/// render settings, then recommends a quality preset and lets the
/// user pick one before starting the maze. A collapsible "Custom"
/// panel allows fine-tuning individual settings with a live FPS
/// prediction at the bottom.
/// </summary>
public class CalibrationForm : Form
{
    // ── Layout constants ───────────────────────────────────────────
    private const int FormWidth = 540;
    private const int CollapsedHeight = 430;
    private const int ExpandedHeight = 730;

    // ── Top-level controls ─────────────────────────────────────────
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Label _resultLabel;
    private readonly Panel _presetPanel;
    private readonly Button _startButton;
    private readonly RadioButton[] _presetRadios;
    private readonly RadioButton _customRadio;

    // ── Advanced panel ─────────────────────────────────────────────
    private readonly GroupBox _advancedGroup;
    private ComboBox _cboResolution;
    private ComboBox _cboScale;
    private ComboBox _cboSpp;
    private ComboBox _cboMaxSamples;
    private ComboBox _cboMotionCap;
    private ComboBox _cboFilter;
    private ComboBox _cboTileSize;
    private CheckBox _chkJitter;
    private CheckBox _chkEdgeAware;
    private CheckBox _chkChecker;
    private CheckBox _chkEma;
    private CheckBox _chkClamp;
    private ComboBox _cboLighting;
    private ComboBox _cboSmokeMode;
    private ComboBox _cboVolumetricQuality;
    private Label _lblThrottle;
    private ComboBox _cboThrottle;
    private Label _lblEffective;
    private Label _lblAdvancedFps;

    // ── Dropdown option tables ─────────────────────────────────────
    private static readonly (string label, int w, int h)[] Resolutions =
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

    // ── State ──────────────────────────────────────────────────────
    private readonly Tracable[] _scene;
    private readonly Camera _camera;
    private readonly Light[] _lights;
    private double _raysPerSecond;

    public RenderPreset ChosenPreset { get; private set; } = RenderPreset.Medium;

    // ================================================================
    public CalibrationForm(Tracable[] scene, Camera camera, Light[] lights)
    {
        _scene = scene;
        _lights = lights;
        _camera = camera;

        Text = "CPU Ray Tracer - Performance Calibration";
        ClientSize = new Size(FormWidth, CollapsedHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;

        // ── Header ─────────────────────────────────────────────────
        _statusLabel = new Label
        {
            Text = "Calibrating your CPU...",
            Location = new Point(20, 20),
            Size = new Size(500, 25),
            Font = new Font(Font.FontFamily, 11, FontStyle.Bold)
        };
        _progressBar = new ProgressBar
        {
            Location = new Point(20, 55),
            Size = new Size(500, 28),
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100
        };
        _resultLabel = new Label
        {
            Text = "",
            Location = new Point(20, 93),
            Size = new Size(500, 25)
        };

        // ── Preset radio buttons (+ Custom) ────────────────────────
        _presetPanel = new Panel
        {
            Location = new Point(20, 125),
            Size = new Size(500, 170),
            Visible = false
        };

        var presets = RenderPreset.All;
        int presetPanelHeight = 8 + (presets.Length + 1) * 30 + 6;
        _presetPanel.Size = new Size(500, presetPanelHeight);
        _presetRadios = new RadioButton[presets.Length];
        for (int i = 0; i < presets.Length; i++)
        {
            var p = presets[i];
            _presetRadios[i] = new RadioButton
            {
                Text = FormatPresetLabel(p),
                Location = new Point(10, 8 + i * 30),
                Size = new Size(480, 25),
                Tag = p
            };
            _presetRadios[i].CheckedChanged += OnPresetRadioChanged;
            _presetPanel.Controls.Add(_presetRadios[i]);
        }

        _customRadio = new RadioButton
        {
            Text = "Custom (advanced settings)",
            Location = new Point(10, 8 + presets.Length * 30),
            Size = new Size(480, 25)
        };
        _customRadio.CheckedChanged += OnCustomRadioChanged;
        _presetPanel.Controls.Add(_customRadio);

        // ── Advanced settings GroupBox ──────────────────────────────
        _advancedGroup = BuildAdvancedPanel();

        // Seed the dropdowns with Medium defaults
        PopulateAdvancedFrom(RenderPreset.Medium);

        // ── CPU throttle dropdown ──────────────────────────────────
        _lblThrottle = new Label
        {
            Text = "CPU usage:",
            Location = new Point(20, _presetPanel.Bottom + 10),
            AutoSize = true,
            Visible = false
        };
        _cboThrottle = Cbo(120, _presetPanel.Bottom + 6, 230, ThrottleOptions.Select(t => t.label));
        _cboThrottle.Visible = false;

        // ── Start button ───────────────────────────────────────────
        _startButton = new Button
        {
            Text = "Start Maze",
            Size = new Size(120, 35),
            Location = new Point(210, _presetPanel.Bottom + 36),
            Enabled = false
        };
        _startButton.Click += (_, _) =>
        {
            if (_customRadio.Checked)
                ChosenPreset = BuildPresetFromAdvanced();
            else
                ChosenPreset = ChosenPreset with { ThrottleCpu = ThrottleOptions[Safe(_cboThrottle)].value };
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.AddRange([_statusLabel, _progressBar, _resultLabel,
                           _presetPanel, _lblThrottle, _cboThrottle, _advancedGroup, _startButton]);

        Load += async (_, _) => await RunCalibrationAsync();
    }

    // ── Advanced panel construction ────────────────────────────────
    private GroupBox BuildAdvancedPanel()
    {
        var grp = new GroupBox
        {
            Text = "Advanced Settings",
            Location = new Point(20, 420),
            Size = new Size(500, 270),
            Visible = false
        };

        // Row 0: Resolution & Scale
        grp.Controls.Add(Lbl("Resolution", 15, 25));
        _cboResolution = Cbo(105, 22, 120, Resolutions.Select(r => r.label));
        grp.Controls.Add(_cboResolution);
        grp.Controls.Add(Lbl("Scale", 250, 25));
        _cboScale = Cbo(300, 22, 75, Scales.Select(s => s.label));
        grp.Controls.Add(_cboScale);

        // Row 1: Samples per pass & Max samples
        grp.Controls.Add(Lbl("Samples/pass", 15, 55));
        _cboSpp = Cbo(115, 52, 65, SppOptions.Select(v => v.ToString()));
        grp.Controls.Add(_cboSpp);
        grp.Controls.Add(Lbl("Max samples", 250, 55));
        _cboMaxSamples = Cbo(345, 52, 65, MaxSampleOptions.Select(v => v.ToString()));
        grp.Controls.Add(_cboMaxSamples);

        // Row 2: Motion cap & Filter
        grp.Controls.Add(Lbl("Motion cap", 15, 85));
        _cboMotionCap = Cbo(115, 82, 65, MotionCapOptions.Select(v => v.ToString()));
        grp.Controls.Add(_cboMotionCap);
        grp.Controls.Add(Lbl("Filter", 250, 85));
        _cboFilter = Cbo(300, 82, 75, FilterOptions.Select(f => f.label));
        grp.Controls.Add(_cboFilter);

        // Row 3: Tile size & Lighting
        grp.Controls.Add(Lbl("Tile size", 15, 115));
        _cboTileSize = Cbo(115, 112, 65, TileSizeOptions.Select(v => v.ToString()));
        grp.Controls.Add(_cboTileSize);
        grp.Controls.Add(Lbl("Lighting", 250, 115));
        _cboLighting = Cbo(315, 112, 90, LightingOptions.Select(l => l.label));
        grp.Controls.Add(_cboLighting);

        // Row 4: Smoke mode & volumetric quality
        grp.Controls.Add(Lbl("Smoke", 15, 145));
        _cboSmokeMode = Cbo(115, 142, 130, SmokeModeOptions.Select(s => s.label));
        grp.Controls.Add(_cboSmokeMode);
        grp.Controls.Add(Lbl("Volume", 270, 145));
        _cboVolumetricQuality = Cbo(335, 142, 90, VolumetricQualityOptions.Select(v => v.label));
        grp.Controls.Add(_cboVolumetricQuality);

        // Row 5: Checkboxes
        _chkJitter = new CheckBox
        {
            Text = "Sub-pixel jitter",
            Location = new Point(15, 170),
            Size = new Size(200, 22)
        };
        _chkEdgeAware = new CheckBox
        {
            Text = "Edge-aware filter",
            Location = new Point(250, 170),
            Size = new Size(200, 22)
        };
        grp.Controls.Add(_chkJitter);
        grp.Controls.Add(_chkEdgeAware);

        // Row 6: Noise-reduction checkboxes
        _chkChecker = new CheckBox
        {
            Text = "Checker motion",
            Location = new Point(15, 195),
            Size = new Size(150, 22)
        };
        _chkEma = new CheckBox
        {
            Text = "Temporal EMA",
            Location = new Point(170, 195),
            Size = new Size(150, 22)
        };
        _chkClamp = new CheckBox
        {
            Text = "Sample clamp",
            Location = new Point(330, 195),
            Size = new Size(150, 22)
        };
        grp.Controls.Add(_chkChecker);
        grp.Controls.Add(_chkEma);
        grp.Controls.Add(_chkClamp);

        // Row 7: Live info
        _lblEffective = new Label
        {
            Location = new Point(15, 220),
            Size = new Size(460, 18)
        };
        _lblAdvancedFps = new Label
        {
            Location = new Point(15, 238),
            Size = new Size(460, 20),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold)
        };
        grp.Controls.Add(_lblEffective);
        grp.Controls.Add(_lblAdvancedFps);

        // Wire every control to the live FPS updater
        EventHandler refresh = (_, _) => UpdateAdvancedFps();
        _cboResolution.SelectedIndexChanged += refresh;
        _cboScale.SelectedIndexChanged += refresh;
        _cboSpp.SelectedIndexChanged += refresh;
        _cboMaxSamples.SelectedIndexChanged += refresh;
        _cboMotionCap.SelectedIndexChanged += refresh;
        _cboFilter.SelectedIndexChanged += refresh;
        _cboTileSize.SelectedIndexChanged += refresh;
        _cboLighting.SelectedIndexChanged += refresh;
        _cboSmokeMode.SelectedIndexChanged += refresh;
        _cboVolumetricQuality.SelectedIndexChanged += refresh;
        _chkJitter.CheckedChanged += refresh;
        _chkEdgeAware.CheckedChanged += refresh;
        _chkChecker.CheckedChanged += refresh;
        _chkEma.CheckedChanged += refresh;
        _chkClamp.CheckedChanged += refresh;

        return grp;
    }

    // ── Calibration ────────────────────────────────────────────────
    private async Task RunCalibrationAsync()
    {
        var progress = new Progress<double>(p =>
            _progressBar.Value = Math.Clamp((int)(p * 100), 0, 100));

        // Measure baseline throughput at the low preset for overall guidance.
        _raysPerSecond = await PerformanceCalibrator.MeasureRaysPerSecondAsync(
            _scene, _camera, _lights, TimeSpan.FromSeconds(5), benchPreset: null, progress: progress);

        _progressBar.Value = 100;
        _statusLabel.Text = "Calibration complete!";
        _resultLabel.Text = $"Your CPU (low preset): {_raysPerSecond:#,##0} rays/sec";

        // For a more accurate per-preset estimate, run a short 1s probe
        // at each preset's effective settings. This captures non-linear
        // cost differences (lighting, filters, etc.) that simple ray
        // scaling misses.
        var presets = RenderPreset.All;
        var perPresetRps = new Dictionary<string, double>();
        for (int i = 0; i < presets.Length; i++)
        {
            var p = presets[i];
            _statusLabel.Text = $"Probing preset '{p.Name}'...";
            // Short probe (1s) — enough for a stable reading without annoying delay
            double rps = await PerformanceCalibrator.MeasureRaysPerSecondAsync(
                _scene, _camera, _lights, TimeSpan.FromSeconds(1), p);
            perPresetRps[p.Name] = rps;
        }

        // Choose the highest preset that achieves >=10 FPS using its own probe.
        RenderPreset recommended = RenderPreset.Low;
        foreach (var p in presets)
        {
            if (p.IsDebugSmokePreset)
                continue;

            double fps = p.PredictFps(perPresetRps[p.Name]);
            if (fps >= 10)
            {
                recommended = p;
                break;
            }
        }
        ChosenPreset = recommended;

        for (int i = 0; i < _presetRadios.Length; i++)
        {
            var p = (RenderPreset)_presetRadios[i].Tag!;
            double fps = p.PredictFps(perPresetRps[p.Name]);
            string label = FormatPresetLabel(p) + $"  ~{fps:F1} FPS";
            if (p.Name == recommended.Name)
                label += "  <-- Recommended";
            _presetRadios[i].Text = label;
            if (p.Name == recommended.Name)
                _presetRadios[i].Checked = true;
        }

        _presetPanel.Visible = true;
        _lblThrottle.Visible = true;
        _cboThrottle.Visible = true;
        _startButton.Enabled = true;
    }

    // ── Preset / Custom toggling ───────────────────────────────────
    private void OnPresetRadioChanged(object? sender, EventArgs e)
    {
        if (sender is RadioButton { Checked: true, Tag: RenderPreset p })
        {
            ChosenPreset = p;
            PopulateAdvancedFrom(p);
            CollapseAdvanced();
        }
    }

    private void OnCustomRadioChanged(object? sender, EventArgs e)
    {
        if (_customRadio.Checked)
        {
            ExpandAdvanced();
            UpdateAdvancedFps();
        }
    }

    private void CollapseAdvanced()
    {
        _advancedGroup.Visible = false;
        _startButton.Location = new Point(210, _presetPanel.Bottom + 36);
        ClientSize = new Size(FormWidth, CollapsedHeight);
    }

    private void ExpandAdvanced()
    {
        _advancedGroup.Visible = true;
        _startButton.Location = new Point(210, 690);
        ClientSize = new Size(FormWidth, ExpandedHeight);
    }

    // ── Advanced <-> RenderPreset mapping ──────────────────────────
    private void PopulateAdvancedFrom(RenderPreset p)
    {
        _cboResolution.SelectedIndex = SafeFind(
            Array.FindIndex(Resolutions, r => r.w == p.RenderWidth && r.h == p.RenderHeight),
            Resolutions.Length);
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
        _cboSmokeMode.SelectedIndex = SafeFind(
            Array.FindIndex(SmokeModeOptions, s => s.mode == p.SmokeMode),
            SmokeModeOptions.Length, 1);
        _cboVolumetricQuality.SelectedIndex = SafeFind(
            Array.FindIndex(VolumetricQualityOptions, v => v.quality == p.Volumetrics.Quality),
            VolumetricQualityOptions.Length, 2);
        _chkJitter.Checked = p.SubPixelJitter;
        _chkEdgeAware.Checked = p.EdgeAwareFilter;
        _chkChecker.Checked = p.CheckerboardMotion;
        _chkEma.Checked = p.TemporalBlendAlpha > 0f;
        _chkClamp.Checked = p.SampleClamp > 0f;
    }

    private RenderPreset BuildPresetFromAdvanced()
    {
        var (_, rw, rh) = Resolutions[Safe(_cboResolution)];
        float scale = Scales[Safe(_cboScale)].value;
        int spp = SppOptions[Safe(_cboSpp)];
        uint maxSamples = MaxSampleOptions[Safe(_cboMaxSamples)];
        uint motionCap = MotionCapOptions[Safe(_cboMotionCap)];
        int filterRadius = FilterOptions[Safe(_cboFilter)].radius;
        int tileSize = TileSizeOptions[Safe(_cboTileSize)];
        var lighting = LightingOptions[Safe(_cboLighting)].mode;
        var smokeMode = SmokeModeOptions[Safe(_cboSmokeMode)].mode;
        var volumetricQuality = VolumetricQualityOptions[Safe(_cboVolumetricQuality)].quality;
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(volumetricQuality, smokeMode);

        return new RenderPreset("Custom", rw, rh, spp, maxSamples, motionCap,
            tileSize, _chkJitter.Checked, scale, filterRadius, _chkEdgeAware.Checked,
            lighting,
            CheckerboardMotion: _chkChecker.Checked,
            TemporalBlendAlpha: _chkEma.Checked ? 0.05f : 0f,
            SampleClamp: _chkClamp.Checked ? 10f : 0f,
            ThrottleCpu: ThrottleOptions[Safe(_cboThrottle)].value,
            SmokeMode: smokeMode,
            Volumetrics: volumetrics);
    }

    private void UpdateAdvancedFps()
    {
        if (_raysPerSecond <= 0)
        {
            _lblEffective.Text = "";
            _lblAdvancedFps.Text = "Waiting for calibration...";
            return;
        }
        var p = BuildPresetFromAdvanced();
        double fps = p.PredictFps(_raysPerSecond);
        _lblEffective.Text = $"Effective resolution: {p.EffectiveWidth} x {p.EffectiveHeight}    " +
                             $"Rays/frame: {p.RaysPerFrame:#,##0}";
        _lblAdvancedFps.Text = $"Estimated: ~{fps:F1} FPS";
        _lblAdvancedFps.ForeColor = fps >= 10 ? Color.DarkGreen
                                  : fps >= 5  ? Color.DarkGoldenrod
                                              : Color.DarkRed;
    }

    // ── Tiny helpers ───────────────────────────────────────────────
    private static string FormatPresetLabel(RenderPreset p)
        => $"{p.Name}  ({p.EffectiveWidth}x{p.EffectiveHeight}, {p.SppPerJob} spp, {p.FeatureSummary})";

    private static int SafeFind(int index, int count, int fallback = 0)
        => index >= 0 && index < count ? index : fallback;

    private static int Safe(ComboBox cbo)
        => Math.Clamp(cbo.SelectedIndex, 0, cbo.Items.Count - 1);

    private static Label Lbl(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true
    };

    private static ComboBox Cbo(int x, int y, int w, IEnumerable<string> items)
    {
        var c = new ComboBox
        {
            Location = new Point(x, y),
            Size = new Size(w, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        c.Items.AddRange(items.ToArray());
        if (c.Items.Count > 0) c.SelectedIndex = 0;
        return c;
    }
}
