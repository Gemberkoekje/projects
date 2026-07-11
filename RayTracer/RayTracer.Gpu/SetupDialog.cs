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

/// <summary>
/// App-wide, persisted settings shared by the normal launch (config-then-run, see
/// <c>Program.RunApp</c>) and the screensaver switches (<see cref="Screensaver"/>).
/// Saved as JSON under <c>%APPDATA%\RayTracer.Gpu</c> so a choice made in the setup
/// dialog survives across launches — the GPU equivalent of the original app's
/// <c>CalibrationForm</c> preset, minus the CPU benchmarking the GPU doesn't need.
/// </summary>
internal sealed class AppSettings
{
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool Fullscreen { get; set; }

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

    // ── Classic props & animations (orthogonal to Style — available in every look).
    //    Defaults match the original screensaver: props on, overhead map off. ──
    public bool ShowRat { get; set; } = true;
    public bool ShowOpenGlLogo { get; set; } = true;
    public bool ShowWallSigns { get; set; } = true;
    public bool ShowOverheadMap { get; set; }
    public bool BumpyWalls { get; set; } = true;

    /// <summary>Walls rise from the floor when a maze starts.</summary>
    public bool MazeBuildInAnim { get; set; } = true;

    /// <summary>On reaching the goal, generate a fresh maze and keep going.</summary>
    public bool MazeRegenerate { get; set; } = true;

    /// <summary>Play a short completion transition before regenerating.</summary>
    public bool MazeOutroAnim { get; set; } = true;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RayTracer.Gpu", "settings.json");

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
        Width = Width,
        Height = Height,
        Fullscreen = Fullscreen,
        Style = Style,
        SmokeMode = SmokeMode,
        StartView = StartView,
        MazeSize = MazeSize,
        FogDrift = FogDrift,
        SampleClamp = SampleClamp,
        StillTime = StillTime,
        ShowRat = ShowRat,
        ShowOpenGlLogo = ShowOpenGlLogo,
        ShowWallSigns = ShowWallSigns,
        ShowOverheadMap = ShowOverheadMap,
        BumpyWalls = BumpyWalls,
        MazeBuildInAnim = MazeBuildInAnim,
        MazeRegenerate = MazeRegenerate,
        MazeOutroAnim = MazeOutroAnim,
    };
}

/// <summary>
/// The startup configuration dialog — the single setup screen the app shows before it
/// runs (and the same one the screensaver <c>/c</c> switch opens). Lets the user pick
/// the look (Classic / Enhanced), resolution / fullscreen, the smoke mode, the starting
/// debug view, maze size, the motion / firefly knobs, and the classic props &amp;
/// animations, then <b>Start</b> runs the full renderer. Choices are read back into
/// <see cref="Result"/>.
/// </summary>
internal sealed class SetupDialog : Form
{
    private static readonly (string Label, RenderStyle Style)[] Styles =
    [
        ("Enhanced (spectral lighting)", RenderStyle.Enhanced),
        ("Classic (unlit, like the original)", RenderStyle.Classic),
    ];

    private static readonly (string Label, int W, int H)[] Resolutions =
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

    public AppSettings Result { get; }

    private readonly ComboBox _style;
    private readonly ComboBox _resolution;
    private readonly CheckBox _fullscreen;
    private readonly ComboBox _smoke;
    private readonly ComboBox _startView;
    private readonly NumericUpDown _mazeSize;
    private readonly NumericUpDown _fogDrift;
    private readonly NumericUpDown _clamp;
    private readonly Label _smokeLabel;
    private readonly Label _fogLabel;
    private readonly Label _clampLabel;

    private readonly CheckBox _showRat;
    private readonly CheckBox _showLogo;
    private readonly CheckBox _showSigns;
    private readonly CheckBox _showMap;
    private readonly CheckBox _bumpy;
    private readonly CheckBox _buildIn;
    private readonly CheckBox _regen;
    private readonly CheckBox _outro;

    public SetupDialog(AppSettings settings)
    {
        Result = settings.Clone();

        Text = "Spectral Maze — GPU ray tracer";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 536);

        var title = new Label
        {
            Text = "Spectral Maze",
            Left = 16, Top = 14, Width = 388, Height = 26,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
        };
        var subtitle = new Label
        {
            Text = "GPU ray tracer — choose your settings, then Start.",
            Left = 16, Top = 42, Width = 388, Height = 20,
            ForeColor = SystemColors.GrayText,
        };

        int y = 78;
        const int rowH = 34;
        Label Row(string text)
        {
            var l = new Label { Text = text, Left = 16, Top = y + 3, Width = 150, Height = 22 };
            return l;
        }

        _style = new ComboBox { Left = 172, Top = y, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var s in Styles) _style.Items.Add(s.Label);
        _style.SelectedIndex = Math.Max(0, Array.FindIndex(Styles, s => s.Style == Result.Style));
        Label styleLabel = Row("Look");
        y += rowH;

        _resolution = new ComboBox { Left = 172, Top = y, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var r in Resolutions) _resolution.Items.Add(r.Label);
        _resolution.SelectedIndex = Math.Max(0, Array.FindIndex(Resolutions,
            r => r.W == Result.Width && r.H == Result.Height));
        Label resLabel = Row("Resolution");
        y += rowH;

        _fullscreen = new CheckBox { Left = 172, Top = y, Width = 232, Text = "Fullscreen (borderless)", Checked = Result.Fullscreen };
        Label fsLabel = Row("Display");
        y += rowH;

        _smoke = new ComboBox { Left = 172, Top = y, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var s in SmokeOptions) _smoke.Items.Add(s.Label);
        _smoke.SelectedIndex = Math.Max(0, Array.FindIndex(SmokeOptions, s => s.Mode == Result.SmokeMode));
        _smokeLabel = Row("Smoke");
        y += rowH;

        _startView = new ComboBox { Left = 172, Top = y, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (Phase5DebugMode v in Views) _startView.Items.Add(v);
        _startView.SelectedItem = Result.StartView;
        Label viewLabel = Row("Start view");
        y += rowH;

        _mazeSize = new NumericUpDown { Left = 172, Top = y, Width = 232, Minimum = 8, Maximum = 48, Value = Math.Clamp(Result.MazeSize, 8, 48) };
        Label mazeLabel = Row("Maze size");
        y += rowH;

        _fogDrift = new NumericUpDown { Left = 172, Top = y, Width = 232, Minimum = 0m, Maximum = 5m, DecimalPlaces = 1, Increment = 0.1m, Value = (decimal)Math.Clamp(Result.FogDrift, 0f, 5f) };
        _fogLabel = Row("Fog drift speed");
        y += rowH;

        _clamp = new NumericUpDown { Left = 172, Top = y, Width = 232, Minimum = 0m, Maximum = 20m, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)Math.Clamp(Result.SampleClamp, 0f, 20f) };
        _clampLabel = Row("Firefly clamp");
        y += rowH + 6;

        // ── Classic props & animations (checkboxes, two columns) ──
        var propsBox = new GroupBox { Left = 16, Top = y, Width = 388, Height = 132, Text = "Classic props & animations" };
        _showRat = new CheckBox { Left = 12, Top = 22, Width = 176, Text = "Rat", Checked = Result.ShowRat };
        _showLogo = new CheckBox { Left = 12, Top = 48, Width = 176, Text = "OpenGL logo", Checked = Result.ShowOpenGlLogo };
        _showSigns = new CheckBox { Left = 12, Top = 74, Width = 176, Text = "Wall signs", Checked = Result.ShowWallSigns };
        _showMap = new CheckBox { Left = 12, Top = 100, Width = 176, Text = "Overhead map", Checked = Result.ShowOverheadMap };
        _bumpy = new CheckBox { Left = 198, Top = 22, Width = 182, Text = "Bumpy walls", Checked = Result.BumpyWalls };
        _buildIn = new CheckBox { Left = 198, Top = 48, Width = 182, Text = "Wall build-in intro", Checked = Result.MazeBuildInAnim };
        _regen = new CheckBox { Left = 198, Top = 74, Width = 182, Text = "Regenerate on finish", Checked = Result.MazeRegenerate };
        _outro = new CheckBox { Left = 198, Top = 100, Width = 182, Text = "Completion animation", Checked = Result.MazeOutroAnim };
        propsBox.Controls.AddRange([_showRat, _showLogo, _showSigns, _showMap, _bumpy, _buildIn, _regen, _outro]);
        y += propsBox.Height + 10;

        var start = new Button { Text = "Start", Left = 234, Top = y, Width = 82, Height = 28, DialogResult = DialogResult.OK };
        var exit = new Button { Text = "Exit", Left = 322, Top = y, Width = 82, Height = 28, DialogResult = DialogResult.Cancel };
        start.Click += (_, _) =>
        {
            Result.Style = Styles[_style.SelectedIndex].Style;
            (_, int w, int h) = Resolutions[_resolution.SelectedIndex];
            Result.Width = w;
            Result.Height = h;
            Result.Fullscreen = _fullscreen.Checked;
            Result.SmokeMode = SmokeOptions[_smoke.SelectedIndex].Mode;
            Result.StartView = (Phase5DebugMode)_startView.SelectedItem!;
            Result.MazeSize = (int)_mazeSize.Value;
            Result.FogDrift = (float)_fogDrift.Value;
            Result.SampleClamp = (float)_clamp.Value;
            Result.ShowRat = _showRat.Checked;
            Result.ShowOpenGlLogo = _showLogo.Checked;
            Result.ShowWallSigns = _showSigns.Checked;
            Result.ShowOverheadMap = _showMap.Checked;
            Result.BumpyWalls = _bumpy.Checked;
            Result.MazeBuildInAnim = _buildIn.Checked;
            Result.MazeRegenerate = _regen.Checked;
            Result.MazeOutroAnim = _outro.Checked;
        };

        // Classic is unlit and smoke-free, so the smoke / fog / firefly knobs don't
        // apply — grey them out when Classic is selected.
        void SyncEnabled()
        {
            bool classic = Styles[_style.SelectedIndex].Style == RenderStyle.Classic;
            foreach (Control c in new Control[] { _smoke, _smokeLabel, _fogDrift, _fogLabel, _clamp, _clampLabel })
                c.Enabled = !classic;
        }
        _style.SelectedIndexChanged += (_, _) => SyncEnabled();
        SyncEnabled();

        Controls.AddRange([
            title, subtitle,
            styleLabel, _style,
            resLabel, _resolution,
            fsLabel, _fullscreen,
            _smokeLabel, _smoke,
            viewLabel, _startView,
            mazeLabel, _mazeSize,
            _fogLabel, _fogDrift,
            _clampLabel, _clamp,
            propsBox,
            start, exit,
        ]);
        AcceptButton = start;
        CancelButton = exit;
    }
}
