using System.Diagnostics;
using System.Globalization;
using Vista.Atmosphere;
using Vista.Astronomy;
using Vista.Cli;
using Vista.Composition;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.Sliders;
using Vista.Tweak;
using Vista.World;

namespace Vista.App.Window;

/// <summary>
/// The preview and tweak window (vista-plan §3.1, §10 M1.16): every control of M1 by hand, on one <see cref="TweakRunner"/> that keeps the
/// session's stages. A change restarts the progressive preview, which refines pass by pass; the set pieces and the seed start a new scene,
/// whose camera the auto camera chooses (the default camera without mountains). The sliders show their value, auto or pinned, and an
/// implausible mark, with a reset each; the camera nudges in metres, turns in degrees and never goes into the ground or the forest; the
/// estimate and the final run on the kept stages. The scene is the CLI's own files: the window writes them before each run, and saving
/// writes them where the user says, so the CLI renders what the window shows.
/// </summary>
internal sealed class TweakWindow : Form
{
    private const string WorkName = "scene";
    private static readonly double[] NudgeSteps = [0.5, 1.5, 5, 25, 100, 500];
    private static readonly double[] TurnSteps = [0.5, 1, 5, 15];
    private static readonly int[] FinalWidths = [1920, 2560, 3840];
    private static readonly SetPiece[] Pieces = Enum.GetValues<SetPiece>();
    private static readonly Season[] Seasons = Enum.GetValues<Season>();
    private static readonly TimeOfDay[] Times = Enum.GetValues<TimeOfDay>();
    private static readonly TweakView[] Views = [.. Enum.GetValues<TweakView>().Where(v => v != TweakView.None)];

    private readonly TweakRunner _runner;
    private readonly string _work;
    private readonly TweakPaths _workPaths;
    private readonly string _previewOut;
    private readonly ToolTip _tips = new() { AutoPopDelay = 30000, InitialDelay = 400 };
    private readonly Random _random = new();
    private readonly List<SliderRow> _rows = [];
    private TweakScene _scene;
    private TweakPaths? _saved;
    private TweakJob? _choosing;
    private TweakJob? _previewJob;
    private TweakJob? _explicitJob;
    private TweakJob? _estimateJob;
    private (TweakScene Scene, EstimateInfo Estimate)? _estimate;
    private CameraGround? _ground;
    private WorldFooting? _footing;
    private bool _showing;

    // A change is run once the controls have been still for a moment, so a slider's drag is one preview, not one a step.
    private readonly System.Windows.Forms.Timer _settle = new() { Interval = 200 };

    // The controls the window sets from its scene, in a column that scrolls.
    private readonly FlowLayoutPanel _controls = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(4),
    };
    private readonly PreviewPanel _preview = new() { Dock = DockStyle.Fill };
    private readonly TextBox _command = new() { Dock = DockStyle.Bottom, ReadOnly = true, BorderStyle = BorderStyle.None };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _progress = new() { Visible = false, Width = 200 };
    private readonly CheckBox[] _pieces = [.. Pieces.Select(p => new CheckBox { Text = Words(p), AutoSize = true, Margin = new Padding(3, 2, 12, 2) })];
    private readonly NumericUpDown _seed = new() { Maximum = ulong.MaxValue, Width = 140 };
    private readonly Label _hints = new() { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Color.FromArgb(0xB2, 0x6A, 0x00) };
    private readonly ComboBox _season = Combo(Seasons.Select(s => JsonName(s)));
    private readonly ComboBox _time = Combo(Times.Select(t => JsonName(t)));
    private readonly TrackBar _snow = Track();
    private readonly TrackBar _haze = Track();
    private readonly ComboBox _place = Combo([.. Places.All.Select(p => p.Name), "custom latitude"]);
    private readonly NumericUpDown _latitude = new() { Minimum = -90, Maximum = 90, DecimalPlaces = 2, Increment = 0.5m, Width = 70 };
    private readonly TrackBar _altitude = Track();
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
    private readonly TrackBar _solarTime = Track();
    private readonly TrackBar _temperature = Track();
    private readonly CheckBox _fahrenheit = new() { Text = "show °F", AutoSize = true };
    private readonly TrackBar _humidity = Track();
    private readonly TrackBar _valleyHaze = Track();
    private readonly TrackBar _mist = Track();
    private readonly Panel _advanced = new() { AutoSize = true, Visible = false };
    private readonly CheckBox _advancedToggle = new() { Text = "Advanced: place, date, weather", AutoSize = true };
    private readonly Label _camera = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly Label _cameraNote = new() { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Color.FromArgb(0xB2, 0x6A, 0x00) };
    private readonly ComboBox _nudgeStep = Combo(NudgeSteps.Select(s => Metres(s)));
    private readonly ComboBox _turnStep = Combo(TurnSteps.Select(s => string.Create(CultureInfo.InvariantCulture, $"{s}°")));
    private readonly NumericUpDown _focal = Number(10, 1200, 1, 1);
    private readonly NumericUpDown _focus = Number(1, 100_000, 10, 0);
    private readonly NumericUpDown _fStop = Number(0, 64, 0.5m, 1);
    private readonly NumericUpDown _exposure = Number(-10, 10, 0.1m, 1);
    private readonly Button _autoCamera = new() { Text = "Auto camera", AutoSize = true };
    private readonly List<Control> _cameraControls = [];
    private readonly ComboBox _view = Combo(Views.Select(v => JsonName(v)));
    private readonly ComboBox _previewWidth = Combo(TweakPreview.Widths.Select(w => $"{w}×{TweakScene.HeightOf(w)}"));
    private readonly ComboBox _previewSpp = Combo(TweakPreview.Samples.Select(s => $"{s} spp"));
    private readonly ComboBox _finalWidth = Combo(FinalWidths.Select(w => $"{w}×{TweakScene.HeightOf(w)}"));
    private readonly ComboBox _finalSpp = Combo(TweakPreview.Samples.Select(s => $"{s} spp"));
    private readonly CheckBox _finalDenoise = new() { Text = "denoise (GPU)", AutoSize = true };
    private readonly NumericUpDown _noise = Number(0, 20, 0.25m, 2);
    private readonly TextBox _finalPath = new() { Width = 330 };
    private readonly Button _estimateButton = new() { Text = "Estimate", AutoSize = true };
    private readonly Button _finalButton = new() { Text = "Render final", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly Label _estimateText = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly Label _finalText = new() { AutoSize = true, MaximumSize = new Size(520, 0) };

    /// <summary>
    /// A window on <paramref name="scene"/>, running on <paramref name="runner"/> (which it disposes when it closes), with its working files
    /// in <paramref name="work"/>.
    /// </summary>
    public TweakWindow(TweakScene scene, TweakRunner runner, string work, TweakPaths? saved = null)
    {
        _scene = scene;
        _runner = runner;
        _work = Path.GetFullPath(work);
        _saved = saved;
        Directory.CreateDirectory(_work);
        _workPaths = TweakPaths.Of(_work, WorkName);
        _previewOut = Path.Combine(_work, "preview.png");
        _finalPath.Text = DefaultFinalPath();

        Text = "Vista";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Size = new Size(1560, 1000);
        MinimumSize = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Layout_();
        _runner.Started += job => Post(() => OnStarted(job));
        _runner.Frame += (job, frame) => Post(() => OnFrame(job, frame));
        _runner.Progress += (_, progress) => Post(() => OnProgress(progress));
        _runner.Finished += result => Post(() => HandleResult(result));
        _runner.Broken += message => Post(() => Status($"No renderer: {message}"));
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Submit();
        };
        Shown += (_, _) =>
        {
            ShowScene();
            Submit();
        };
        FormClosing += (_, _) => _runner.Dispose();
    }

    /// <summary>The scene the window shows.</summary>
    public TweakScene Scene => _scene;

    /// <summary>The runner the window's jobs run on.</summary>
    public TweakRunner Runner => _runner;

    /// <summary>The sidecar of the last preview shown; null before one.</summary>
    public Sidecar? LastPreview { get; private set; }

    /// <summary>The last result the window handled.</summary>
    public TweakResult? LastResult { get; private set; }

    /// <summary>The frames of the last preview shown before its own image.</summary>
    public int FramesShown { get; private set; }

    /// <summary>Why the camera's last edit was moved (lifted out of the ground, kept over the world); null when it was not.</summary>
    public string? CameraNote { get; private set; }

    /// <summary>The sliders as last resolved for the controls.</summary>
    public SliderState? Sliders { get; private set; }

    /// <summary>The last estimate, and the scene it was for.</summary>
    public (TweakScene Scene, EstimateInfo Estimate)? LastEstimate => _estimate;

    /// <summary>True when nothing runs, waits or is about to be asked for.</summary>
    public bool Settled => !_runner.Busy && _choosing is null && !_settle.Enabled;

    /// <summary>The working files' folder.</summary>
    public string WorkFolder => _work;

    /// <summary>Raised after the window handled a job's result.</summary>
    public event Action<TweakResult>? Handled;

    /// <summary>
    /// True for a window a check drives (<see cref="WindowCheck"/>): it opens off the screen and without taking the focus, so the desktop it
    /// runs on stays the user's.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Quiet { get; init; }

    protected override bool ShowWithoutActivation => Quiet;

    protected override void OnLoad(EventArgs e)
    {
        if (Quiet)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(SystemInformation.VirtualScreen.Right + 100, SystemInformation.VirtualScreen.Top);
            ShowInTaskbar = false;
        }
        base.OnLoad(e);
    }

    /// <summary>Applies a change of the scene: the controls follow it, and the preview restarts (a new scene's camera is chosen first).</summary>
    public void Apply(TweakScene next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next == _scene)
            return;
        _scene = next;
        ShowScene();
        _settle.Stop();
        _settle.Start();
    }

    /// <summary>Moves the camera <paramref name="forward"/>, <paramref name="right"/> and <paramref name="up"/> metres, never into the ground.</summary>
    public void NudgeCamera(double forward, double right, double up)
    {
        if (_scene.Camera is { } camera)
            ApplyCamera(CameraMoves.Nudge(camera, forward, right, up));
    }

    /// <summary>Pans, tilts and rolls the camera by these degrees.</summary>
    public void TurnCamera(double pan, double tilt, double roll)
    {
        if (_scene.Camera is { } camera)
            ApplyCamera(CameraMoves.Turn(camera, pan, tilt, roll));
    }

    /// <summary>Runs the auto camera on the scene's world; its pick becomes the scene's camera.</summary>
    public TweakJob ChooseCamera()
    {
        TweakJob job = _runner.Submit(TweakRun.AutoCamera, _scene, _workPaths, _previewOut);
        _explicitJob = job;
        ShowBusy();
        return job;
    }

    /// <summary>Estimates the final at <paramref name="finalPath"/> (writing its <c>.estimate.json</c>), on the session's kept stages.</summary>
    public TweakJob Estimate(string finalPath)
    {
        string out_ = FinalOut(finalPath);
        TweakJob job = _runner.Submit(TweakRun.Estimate, _scene, FinalPaths(out_), out_);
        _explicitJob = _estimateJob = job;
        ShowBusy();
        return job;
    }

    /// <summary>Renders the final to <paramref name="finalPath"/>, with its EXR, its sidecar and its own scene files beside it.</summary>
    public TweakJob RenderFinal(string finalPath)
    {
        string out_ = FinalOut(finalPath);
        TweakJob job = _runner.Submit(TweakRun.Final, _scene, FinalPaths(out_), out_);
        _explicitJob = job;
        Say(_finalText, "Rendering the final…");
        ShowBusy();
        return job;
    }

    /// <summary>Writes the scene's files where <paramref name="recipePath"/> says (<c>name.recipe.json</c> and its siblings).</summary>
    public TweakPaths SaveScene(string recipePath)
    {
        TweakPaths paths = TweakPaths.Beside(recipePath);
        TweakFiles.Write(_scene, paths);
        _saved = paths;
        Text = $"Vista — {paths.Name}";
        if (_finalPath.Text == DefaultFinalPath(null))
            _finalPath.Text = DefaultFinalPath();
        Status($"Saved {paths.Recipe} and its camera, slider and options files.");
        return paths;
    }

    /// <summary>Opens the scene of <paramref name="recipePath"/> and the files beside it.</summary>
    public void LoadScene(string recipePath)
    {
        TweakScene scene = TweakFiles.Read(recipePath, out IReadOnlyList<string> ignored);
        _saved = TweakPaths.Beside(recipePath);
        Text = $"Vista — {_saved.Name}";
        _finalPath.Text = DefaultFinalPath();
        Apply(scene with { Preview = scene.Preview with { View = _scene.Preview.View }, Final = _scene.Final });
        if (ignored.Count > 0)
            Status($"Opened {recipePath}; its options file's {string.Join(", ", ignored)} are not the window's (only --seed, --width, --spp).");
    }

    /// <summary>The window as it looks now, for the log.</summary>
    public Bitmap Snapshot()
    {
        var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
        return bitmap;
    }

    /// <summary>The column of controls as it shows now (scrolled to <paramref name="into"/>'s group: "sliders", "camera" or "final"), for the log.</summary>
    public Bitmap SnapshotControls(string into)
    {
        Control target = into switch
        {
            "final" => _finalText.Visible ? _finalText : _estimateButton,
            "camera" => _autoCamera,
            _ => _hints.Visible ? _hints : _advancedToggle,
        };
        _controls.ScrollControlIntoView(target);
        _controls.PerformLayout();
        var bitmap = new Bitmap(_controls.Width, _controls.Height);
        _controls.DrawToBitmap(bitmap, new Rectangle(0, 0, _controls.Width, _controls.Height));
        return bitmap;
    }

    /// <summary>True while the advanced sliders (place, date, weather) are shown.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AdvancedShown
    {
        get => _advancedToggle.Checked;
        set => _advancedToggle.Checked = value;
    }

    // ---- Layout ----

    private void Layout_()
    {
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("&Open scene…", null, (_, _) => OpenScene(), Keys.Control | Keys.O));
        file.DropDownItems.Add(new ToolStripMenuItem("&Save scene", null, (_, _) => Save(as_: false), Keys.Control | Keys.S));
        file.DropDownItems.Add(new ToolStripMenuItem("Save scene &as…", null, (_, _) => Save(as_: true), Keys.Control | Keys.Shift | Keys.S));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("&Copy the preview's command line", null, (_, _) => CopyCommand()));
        file.DropDownItems.Add(new ToolStripMenuItem("Open the &working folder", null, (_, _) => OpenFolder(_work)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));
        menu.Items.Add(file);
        MainMenuStrip = menu;

        var status = new StatusStrip();
        status.Items.Add(_status);
        status.Items.Add(_progress);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 6 };
        _command.BackColor = Color.FromArgb(22, 22, 22);
        _command.ForeColor = Color.Silver;
        _command.Font = new Font(FontFamily.GenericMonospace, 8.5f);
        split.Panel1.Controls.Add(_preview);
        split.Panel1.Controls.Add(_command);
        _tips.SetToolTip(_command, "The CLI command that renders this preview from the window's working files (File → Copy)");

        FlowLayoutPanel controls = _controls;
        controls.Controls.Add(SceneGroup());
        controls.Controls.Add(SlidersGroup());
        controls.Controls.Add(CameraGroup());
        controls.Controls.Add(ViewGroup());
        controls.Controls.Add(FinalGroup());
        split.Panel2.Controls.Add(controls);

        Controls.Add(split);
        Controls.Add(status);
        Controls.Add(menu);
        Load += (_, _) => split.SplitterDistance = Math.Max(300, split.Width - LogicalToDeviceUnits(580));
    }

    private GroupBox SceneGroup()
    {
        var pieces = new TableLayoutPanel { AutoSize = true, ColumnCount = 3 };
        for (int i = 0; i < Pieces.Length; i++)
        {
            SetPiece piece = Pieces[i];
            CheckBox box = _pieces[i];
            // The scene follows the box; a conflicting piece (a lake and a frozen lake) is swapped out, and its box follows the scene.
            box.CheckedChanged += (_, _) =>
            {
                if (!_showing)
                    Apply(box.Checked ? _scene.With(piece) : _scene.Without(piece));
            };
            _tips.SetToolTip(box, "A set piece: changing them starts a new scene (a new world, its camera chosen again)");
            pieces.Controls.Add(box, i % 3, i / 3);
        }
        _seed.ValueChanged += (_, _) =>
        {
            if (!_showing)
                Apply(_scene.WithSeed((ulong)_seed.Value));
        };
        var newSeed = new Button { Text = "New seed", AutoSize = true };
        newSeed.Click += (_, _) => Apply(_scene.WithSeed(_scene.NextSeed(_random)));
        var seedRow = Flow(new Label { Text = "Seed", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, _seed, newSeed);
        return Group("Scene", Stack(pieces, seedRow));
    }

    private GroupBox SlidersGroup()
    {
        var snowScale = new TrackScale(0, 6000, 50);
        var hazeScale = new TrackScale(SliderPins.MinVisibilityKm, SliderPins.MaxVisibilityKm, 0, Logarithmic: true);
        var altitudeScale = new TrackScale(0, SliderPins.MaxAltitudeMetres, 50);
        var timeScale = new TrackScale(0, 24 * 60 - 5, 5);
        var temperatureScale = new TrackScale(-40, 40, 0.5);
        var humidityScale = new TrackScale(0, 100, 1);
        var mistScale = new TrackScale(SliderPins.MinMistKm, SliderPins.MaxMistKm, 0, Logarithmic: true);
        Setup(_snow, snowScale);
        Setup(_haze, hazeScale);
        Setup(_altitude, altitudeScale);
        Setup(_solarTime, timeScale);
        Setup(_temperature, temperatureScale);
        Setup(_humidity, humidityScale);
        Setup(_valleyHaze, hazeScale);
        _mist.Minimum = 0;
        _mist.Maximum = mistScale.Last + 1; // position 0 is no mist
        _date.MinDate = new DateTime(1900, 1, 1);

        // Basic (vista-plan §3.2): season, time of day, snow, fog.
        var basic = Table();
        AddRow(basic, Slider.Season, "Season", _season, r => _season.SelectedIndex = Array.FindIndex(Seasons, s => JsonName(s) == r.Value),
            "The season: the sun's path and the snow's history. Auto: the pinned date's, or the set pieces'");
        AddRow(basic, Slider.Time, "Time of day", _time, r => _time.SelectedIndex = Array.FindIndex(Times, t => JsonName(t) == r.Value),
            "The time of day: dawn (sun at −4°, rising), day, golden hour (+2°, setting), dusk (−4°, setting), night");
        AddRow(basic, Slider.Snow, "Snow", _snow, r => _snow.Value = snowScale.PositionOf(r.Number ?? 0),
            "Where the season's snow begins, m above sea level. Auto: the climate's snowpack, nudged by the freezing level");
        AddRow(basic, Slider.Haze, "Fog / haze", _haze, r => _haze.Value = hazeScale.PositionOf(r.Number ?? 50),
            "The visibility at the valley floor, km. Auto: the day's humidity");
        _season.SelectedIndexChanged += (_, _) => Pin(Slider.Season, p => p with { Season = Seasons[_season.SelectedIndex] });
        _time.SelectedIndexChanged += (_, _) => Pin(Slider.Time, p => p with { Time = Times[_time.SelectedIndex] });
        _snow.ValueChanged += (_, _) => Pin(Slider.Snow, p => p with { Snowline = snowScale.ValueAt(_snow.Value) });
        _haze.ValueChanged += (_, _) => Pin(Slider.Haze, p => p with { Visibility = hazeScale.ValueAt(_haze.Value) });

        // Advanced (vista-plan §3.3).
        var advanced = Table();
        var placeEditor = Flow(_place, _latitude);
        _place.Width = 140;
        AddRow(advanced, Slider.Location, "Location", placeEditor, r =>
        {
            bool custom = r.Number is not null;
            _place.SelectedIndex = custom ? Places.All.Count : Math.Max(0, IndexOfPlace(r.Value));
            _latitude.Enabled = custom;
            _latitude.Value = (decimal)Math.Clamp(r.Number ?? Places.Get(PlaceOf(r.Value)).LatitudeDegrees, -90, 90);
        }, "The place: the sky and the climate only, never the terrain. Custom latitude falls back to a latitude model");
        AddRow(advanced, Slider.Altitude, "Altitude", _altitude, r => _altitude.Value = altitudeScale.PositionOf(r.Number ?? 0),
            "The altitude of the world's height 0, m: thinner air, a deeper blue. Auto: its valleys at the place's valley floor");
        AddRow(advanced, Slider.Date, "Date", _date, r => _date.Value = DateTime.ParseExact(r.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            "The date. Auto: the season's middle month, for the place's hemisphere");
        AddRow(advanced, Slider.SolarTime, "Solar time", _solarTime, r => _solarTime.Value = timeScale.PositionOf((r.Number ?? 12) * 60.0),
            "The local solar time (12:00 is solar noon). Auto: the time of day's");
        var temperatureEditor = Flow(_temperature, _fahrenheit);
        _temperature.Width = 120;
        AddRow(advanced, Slider.Temperature, "Temperature", temperatureEditor, r => _temperature.Value = temperatureScale.PositionOf(r.Number ?? 10),
            "The air at the valley floor. Auto: the place's climate normals at this hour");
        AddRow(advanced, Slider.Humidity, "Humidity", _humidity, r => _humidity.Value = humidityScale.PositionOf(r.Number ?? 50),
            "The relative humidity at the valley floor, %. Auto: the temperature against the day's dew point");
        AddRow(advanced, Slider.ValleyHaze, "Valley haze", _valleyHaze, r => _valleyHaze.Value = hazeScale.PositionOf(r.Number ?? SliderPins.MaxVisibilityKm),
            "The visibility at the floor of the densest valley's pool of haze, km; all the way right is none. Auto: the night's pool, and what the sun left of it");
        AddRow(advanced, Slider.Mist, "Mist", _mist, r => _mist.Value = r.Number is double km && km > 0.0 ? mistScale.PositionOf(km) + 1 : 0,
            "The visibility in the scene's valley's mist, km; all the way left is none. Auto: where the pools' air cools to its dew point");
        _place.SelectedIndexChanged += (_, _) =>
        {
            _latitude.Enabled = _place.SelectedIndex == Places.All.Count;
            Pin(Slider.Location, p => _place.SelectedIndex == Places.All.Count
                ? p with { Location = Place.None, Latitude = (double)_latitude.Value }
                : p with { Location = Places.All[_place.SelectedIndex].Place, Latitude = null });
        };
        _latitude.ValueChanged += (_, _) =>
        {
            if (_place.SelectedIndex == Places.All.Count)
                Pin(Slider.Location, p => p with { Location = Place.None, Latitude = (double)_latitude.Value });
        };
        _altitude.ValueChanged += (_, _) => Pin(Slider.Altitude, p => p with { Altitude = altitudeScale.ValueAt(_altitude.Value) });
        _date.ValueChanged += (_, _) => Pin(Slider.Date, p => p with { Date = DateOnly.FromDateTime(_date.Value) });
        _solarTime.ValueChanged += (_, _) => Pin(Slider.SolarTime,
            p => p with { SolarTime = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(timeScale.ValueAt(_solarTime.Value))), SunElevation = null });
        _temperature.ValueChanged += (_, _) => Pin(Slider.Temperature, p => p with { Temperature = temperatureScale.ValueAt(_temperature.Value) });
        _fahrenheit.CheckedChanged += (_, _) =>
        {
            if (!_showing)
                Apply(_scene with { Pins = _scene.Pins with { TemperatureUnit = _fahrenheit.Checked ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius } });
        };
        _humidity.ValueChanged += (_, _) => Pin(Slider.Humidity, p => p with { Humidity = humidityScale.ValueAt(_humidity.Value) });
        _valleyHaze.ValueChanged += (_, _) => Pin(Slider.ValleyHaze, p => p with { ValleyHaze = hazeScale.ValueAt(_valleyHaze.Value) });
        _mist.ValueChanged += (_, _) => Pin(Slider.Mist, p => p with { Mist = _mist.Value == 0 ? 0.0 : mistScale.ValueAt(_mist.Value - 1) });

        _advanced.Controls.Add(advanced);
        _advancedToggle.CheckedChanged += (_, _) => _advanced.Visible = _advancedToggle.Checked;
        return Group("Sliders", Stack(basic, _advancedToggle, _advanced, _hints));
    }

    private GroupBox CameraGroup()
    {
        _nudgeStep.SelectedIndex = 1;
        _turnStep.SelectedIndex = 2;
        double Step() => NudgeSteps[Math.Max(0, _nudgeStep.SelectedIndex)];
        double Turn() => TurnSteps[Math.Max(0, _turnStep.SelectedIndex)];
        Button Move(string text, string tip, Action click)
        {
            var b = new Button { Text = text, Width = 74, Height = 28, Margin = new Padding(2) };
            b.Click += (_, _) => click();
            _tips.SetToolTip(b, tip);
            _cameraControls.Add(b);
            return b;
        }
        var moves = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, RowCount = 2 };
        moves.Controls.Add(Move("◀ Left", "Move left", () => NudgeCamera(0, -Step(), 0)), 0, 0);
        moves.Controls.Add(Move("▲ Forward", "Move forward, level", () => NudgeCamera(Step(), 0, 0)), 1, 0);
        moves.Controls.Add(Move("Right ▶", "Move right", () => NudgeCamera(0, Step(), 0)), 2, 0);
        moves.Controls.Add(Move("Up ⤒", "Move up", () => NudgeCamera(0, 0, Step())), 3, 0);
        moves.Controls.Add(Move("▼ Back", "Move back, level", () => NudgeCamera(-Step(), 0, 0)), 1, 1);
        moves.Controls.Add(Move("Down ⤓", "Move down (never into the ground or the forest)", () => NudgeCamera(0, 0, -Step())), 3, 1);
        var turns = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, RowCount = 2 };
        turns.Controls.Add(Move("⟲ Pan", "Pan left", () => TurnCamera(-Turn(), 0, 0)), 0, 0);
        turns.Controls.Add(Move("Tilt ▲", "Tilt up", () => TurnCamera(0, Turn(), 0)), 1, 0);
        turns.Controls.Add(Move("Pan ⟳", "Pan right", () => TurnCamera(Turn(), 0, 0)), 2, 0);
        turns.Controls.Add(Move("Roll ⟲", "Roll left", () => TurnCamera(0, 0, Turn())), 3, 0);
        turns.Controls.Add(Move("Tilt ▼", "Tilt down", () => TurnCamera(0, -Turn(), 0)), 1, 1);
        turns.Controls.Add(Move("Roll ⟳", "Roll right", () => TurnCamera(0, 0, -Turn())), 3, 1);

        var lens = Table(4);
        lens.Controls.Add(Caption("Focal length, mm"), 0, 0);
        lens.Controls.Add(_focal, 1, 0);
        lens.Controls.Add(Caption("Exposure, EV"), 2, 0);
        lens.Controls.Add(_exposure, 3, 0);
        lens.Controls.Add(Caption("Focus, m"), 0, 1);
        lens.Controls.Add(_focus, 1, 1);
        lens.Controls.Add(Caption("Aperture, f/"), 2, 1);
        lens.Controls.Add(_fStop, 3, 1);
        _tips.SetToolTip(_focus, "The focus distance: kept in the camera file, but there is no depth of field yet");
        _tips.SetToolTip(_fStop, "The f-number (0 = pinhole): kept in the camera file, but there is no depth of field yet");
        _tips.SetToolTip(_exposure, "Stops on top of the meter: re-exposes the kept render, never traces again");
        _focal.ValueChanged += (_, _) => EditCamera(c => c with { FocalLengthMm = (double)_focal.Value });
        _focus.ValueChanged += (_, _) => EditCamera(c => c with { FocusDistanceMetres = (double)_focus.Value });
        _fStop.ValueChanged += (_, _) => EditCamera(c => c with { FStop = (double)_fStop.Value });
        _exposure.ValueChanged += (_, _) => EditCamera(c => c with { ExposureEv = (double)_exposure.Value });
        _cameraControls.AddRange([_focal, _focus, _fStop, _exposure]);
        _autoCamera.Click += (_, _) => ChooseCamera();
        _tips.SetToolTip(_autoCamera, "Choose the camera for the mountains: a few hundred candidates scored, the best ten rendered small (about 40 s)");

        var steps = Flow(Caption("Move by"), _nudgeStep, Caption("Turn by"), _turnStep);
        return Group("Camera", Stack(_camera, steps, moves, turns, lens, Flow(_autoCamera), _cameraNote));
    }

    private GroupBox ViewGroup()
    {
        _view.SelectedIndexChanged += (_, _) => Edit(s => s with { Preview = s.Preview with { View = Views[_view.SelectedIndex] } });
        _previewWidth.SelectedIndexChanged += (_, _) => Edit(s => s with { Preview = s.Preview with { Width = TweakPreview.Widths[_previewWidth.SelectedIndex] } });
        _previewSpp.SelectedIndexChanged += (_, _) => Edit(s => s with { Preview = s.Preview with { SamplesPerPixel = TweakPreview.Samples[_previewSpp.SelectedIndex] } });
        _tips.SetToolTip(_view, "What the preview shows: the beauty, the beauty denoised, or a debug image");
        var table = Table(2);
        table.Controls.Add(Caption("Show"), 0, 0);
        table.Controls.Add(_view, 1, 0);
        table.Controls.Add(Caption("Preview"), 0, 1);
        table.Controls.Add(Flow(_previewWidth, _previewSpp), 1, 1);
        return Group("View", table);
    }

    private GroupBox FinalGroup()
    {
        _finalWidth.SelectedIndexChanged += (_, _) => Edit(s => s with { Final = s.Final with { Width = FinalWidths[_finalWidth.SelectedIndex] } });
        _finalSpp.SelectedIndexChanged += (_, _) => Edit(s => s with { Final = s.Final with { SamplesPerPixel = TweakPreview.Samples[_finalSpp.SelectedIndex] } });
        _finalDenoise.CheckedChanged += (_, _) => Edit(s => s with { Final = s.Final with { Denoise = _finalDenoise.Checked } });
        _noise.ValueChanged += (_, _) => Edit(s => s with { Final = s.Final with { NoiseTarget = _noise.Value > 0 ? (double)_noise.Value : null } });
        _tips.SetToolTip(_noise, "A noise target for the estimate, in steps of the 8-bit PNG (0: none): the samples that meet it, and their time");
        var browse = new Button { Text = "…", Width = 30 };
        browse.Click += (_, _) => BrowseFinal();
        _estimateButton.Click += (_, _) => Estimate(_finalPath.Text);
        _finalButton.Click += (_, _) => RenderFinal(_finalPath.Text);
        _cancelButton.Click += (_, _) =>
        {
            if (_explicitJob is { } job)
                _runner.Cancel(job);
        };
        var table = Table(2);
        table.Controls.Add(Caption("Size"), 0, 0);
        table.Controls.Add(Flow(_finalWidth, _finalSpp, _finalDenoise), 1, 0);
        table.Controls.Add(Caption("Noise target"), 0, 1);
        table.Controls.Add(_noise, 1, 1);
        table.Controls.Add(Caption("File"), 0, 2);
        table.Controls.Add(Flow(_finalPath, browse), 1, 2);
        return Group("Final", Stack(table, Flow(_estimateButton, _finalButton, _cancelButton), _estimateText, _finalText));
    }

    // ---- Showing the scene ----

    /// <summary>Sets every control from the scene and the sliders resolved for it, without the controls' events taking it for the user's.</summary>
    private void ShowScene()
    {
        _showing = true;
        try
        {
            for (int i = 0; i < Pieces.Length; i++)
                _pieces[i].Checked = _scene.Recipe.SetPieces.Contains(Pieces[i]);
            _seed.Value = _scene.Seed;
            _fahrenheit.Checked = _scene.Pins.TemperatureUnit == TemperatureUnit.Fahrenheit;
            _view.SelectedIndex = Array.IndexOf(Views, _scene.Preview.View);
            _previewWidth.SelectedIndex = Math.Max(0, TweakPreview.Widths.ToList().IndexOf(_scene.Preview.Width));
            _previewSpp.SelectedIndex = Math.Max(0, TweakPreview.Samples.ToList().IndexOf(_scene.Preview.SamplesPerPixel));
            _finalWidth.SelectedIndex = Math.Max(0, Array.IndexOf(FinalWidths, _scene.Final.Width));
            _finalSpp.SelectedIndex = Math.Max(0, TweakPreview.Samples.ToList().IndexOf(_scene.Final.SamplesPerPixel));
            _finalDenoise.Checked = _scene.Final.Denoise;
            _noise.Value = (decimal)(_scene.Final.NoiseTarget ?? 0.0);
            ShowCamera();
        }
        finally
        {
            _showing = false;
        }
        ShowSliders();
        ShowEstimate();
    }

    private void ShowCamera()
    {
        bool has = _scene.Camera is not null;
        foreach (Control c in _cameraControls)
            c.Enabled = has;
        _autoCamera.Enabled = AutoCamera.IntentOf(_scene.Recipe) != CompositionIntent.None;
        if (_scene.Camera is not { } camera)
        {
            _camera.Text = _choosing is not null ? "A new scene: the auto camera is choosing the camera…" : "A new scene: its first run chooses the camera.";
            return;
        }
        double ground = _ground?.Surface.HeightAt(camera.Position.X, camera.Position.Z) ?? double.NaN;
        string above = double.IsNaN(ground) ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" ({camera.Position.Y - ground:0.#} m above the ground)");
        _camera.Text = string.Create(CultureInfo.InvariantCulture,
            $"At x {camera.Position.X:0.#} m, z {camera.Position.Z:0.#} m, {camera.Position.Y:0.#} m up{above}; heading {camera.YawDegrees:0.#}°, " +
            $"tilt {camera.PitchDegrees:0.#}°, roll {camera.RollDegrees:0.#}°");
        // Shown, not edited: a value the box rounds (an auto camera's 47.3 mm) stays the camera's until the user changes it.
        bool showing = _showing;
        _showing = true;
        try
        {
            _focal.Value = Clamp(_focal, camera.FocalLengthMm);
            _focus.Value = Clamp(_focus, camera.FocusDistanceMetres);
            _fStop.Value = Clamp(_fStop, camera.FStop);
            _exposure.Value = Clamp(_exposure, camera.ExposureEv);
        }
        finally
        {
            _showing = showing;
        }
    }

    /// <summary>The sliders resolved for the scene (as its run will resolve them), shown in their rows and the hints.</summary>
    private void ShowSliders()
    {
        try
        {
            Sliders = LinkedSliders.Resolve(_scene.Recipe, _scene.Pins, AtmosphereModel.Default, _footing);
        }
        catch (Exception e) when (e is ArgumentException or InvalidDataException)
        {
            Say(_hints, e.Message);
            return;
        }
        foreach (SliderRow row in _rows)
        {
            if (Sliders.Readings.TryGetValue(row.Slider, out SliderReading? reading))
                row.Show(reading, Sliders.ImplausibleSliders.Contains(row.Slider), _tips);
        }
        Say(_hints, string.Join(Environment.NewLine, Sliders.Hints.Select(h => "⚠ " + h)));
    }

    private void ShowEstimate()
    {
        if (_estimate is not { } kept)
            return;
        _estimateText.ForeColor = kept.Scene == _scene ? SystemColors.ControlText : SystemColors.GrayText;
        string stale = kept.Scene == _scene ? string.Empty : Environment.NewLine + "(for the scene as it was: estimate again)";
        Say(_estimateText, Describe(kept.Estimate) + stale);
    }

    private void ShowBusy()
    {
        bool busy = _explicitJob is not null;
        _cancelButton.Enabled = busy;
        _estimateButton.Enabled = _finalButton.Enabled = !busy;
    }

    // ---- Edits ----

    private void Edit(Func<TweakScene, TweakScene> change)
    {
        if (!_showing)
            Apply(change(_scene));
    }

    private void Pin(Slider slider, Func<SliderPins, SliderPins> pin)
    {
        if (_showing || _rows.Find(r => r.Slider == slider) is { Showing: true })
            return;
        Apply(_scene with { Pins = pin(_scene.Pins) });
    }

    private void EditCamera(Func<CameraSpec, CameraSpec> change)
    {
        if (!_showing && _scene.Camera is { } camera)
            ApplyCamera(change(camera));
    }

    /// <summary>A camera edit, kept out of the ground and the forest (vista-plan §3.1); the note says when it was moved.</summary>
    private void ApplyCamera(CameraSpec camera)
    {
        string? moved = null;
        if (_ground is { } ground)
            camera = ground.Clamp(camera, out moved);
        CameraNote = moved;
        Say(_cameraNote, moved is null ? string.Empty : $"The camera was {moved}.");
        Apply(_scene with { Camera = camera });
    }

    // ---- Runs ----

    /// <summary>Asks for the run the scene needs now: a new scene's camera first (the auto camera for mountains), else the preview.</summary>
    private void Submit()
    {
        if (_scene.IsNew && AutoCamera.IntentOf(_scene.Recipe) != CompositionIntent.None)
        {
            // One auto camera per world: a change of the sliders while it chooses is rendered once its pick is in.
            if (_choosing is { } choosing && SameWorld(choosing.Scene, _scene))
                return;
            if (_choosing is { } stale)
                _runner.Cancel(stale);
            _choosing = _explicitJob = _runner.Submit(TweakRun.AutoCamera, _scene, _workPaths, _previewOut);
            ShowBusy();
            ShowCamera();
            Status("A new scene: the auto camera is choosing its camera (about 40 s).");
            return;
        }
        if (_choosing is { } old)
        {
            _runner.Cancel(old);
            _choosing = _explicitJob = null;
            ShowBusy();
        }
        _previewJob = _runner.Submit(TweakRun.Preview, _scene, _workPaths, _previewOut);
    }

    private void OnStarted(TweakJob job)
    {
        if (job.Kind == TweakRun.Preview)
        {
            FramesShown = 0;
            Status("Rendering the preview…");
        }
        else
        {
            Status(job.Kind switch
            {
                TweakRun.AutoCamera => "The auto camera is choosing the camera (about 40 s)…",
                TweakRun.Estimate => "Estimating the final: its stages, a probe of its first passes, its preview…",
                _ => "Rendering the final…",
            });
            if (job.Kind == TweakRun.Final)
            {
                _progress.Value = 0;
                _progress.Visible = true;
            }
        }
    }

    private void OnFrame(TweakJob job, PreviewFrame frame)
    {
        if (!ReferenceEquals(job, _previewJob))
            return;
        FramesShown++;
        _preview.Show(PreviewPanel.ToBitmap(frame));
        _preview.Caption = $"{frame.Width}×{frame.Height} · {frame.Samples} of {frame.SamplesPerPixel} samples";
        Status($"Rendering the preview: {frame.Samples} of {frame.SamplesPerPixel} samples a pixel…");
    }

    private void OnProgress(TraceProgress progress)
    {
        _progress.Value = (int)Math.Round(Math.Clamp(progress.Progress, 0.0, 1.0) * 100.0);
        string left = progress.RemainingSeconds is double remaining ? $"about {TimeRange.Describe(remaining)} left" : "first pass";
        Status(string.Create(CultureInfo.InvariantCulture,
            $"Rendering the final: {progress.Progress * 100.0:0}% · {TimeRange.Describe(progress.ElapsedSeconds)} · {left}"));
    }

    private void HandleResult(TweakResult result)
    {
        LastResult = result;
        TweakJob job = result.Job;
        if (ReferenceEquals(job, _explicitJob))
        {
            _explicitJob = null;
            _progress.Visible = false;
            ShowBusy();
        }
        if (ReferenceEquals(job, _choosing))
            _choosing = null;
        if (result.World is { } world)
            _footing = world.Footing;

        if (result.Cancelled)
        {
            if (job.Kind != TweakRun.Preview)
                Status($"Stopped: {Describe(job.Kind)}.");
            if (job.Kind == TweakRun.AutoCamera && _scene.IsNew && _choosing is null)
                _camera.Text = "A new scene: the auto camera was stopped. Press Auto camera, or change the scene.";
        }
        else if (result.Problem is { } problem)
        {
            Status($"{Describe(job.Kind)} failed: {problem}");
            if (job.Kind == TweakRun.Final)
                Say(_finalText, $"The final failed: {problem}");
        }
        else if (result.Sidecar is { } sidecar)
        {
            if (result.World is { } w)
                _ground = CameraGround.Of(w.Surface, sidecar.Materials, sidecar.Atmosphere.GroundAltitudeMetres);
            switch (job.Kind)
            {
                case TweakRun.Preview:
                case TweakRun.AutoCamera:
                    ShowPreview(result, sidecar);
                    break;
                case TweakRun.Estimate:
                    if (sidecar.Estimate is { } estimate)
                        _estimate = (job.Scene, estimate);
                    ShowEstimate();
                    _controls.ScrollControlIntoView(_estimateText);
                    Status($"Estimated in {TimeRange.Describe(result.Seconds)}.");
                    break;
                case TweakRun.Final:
                    ShowFinal(result, sidecar);
                    break;
            }
        }
        Handled?.Invoke(result);
    }

    private void ShowPreview(TweakResult result, Sidecar sidecar)
    {
        TweakJob job = result.Job;
        // A new scene takes the camera its run chose (the auto camera's pick, or the default camera), as does the auto camera button;
        // when the scene moved on meanwhile (a slider), it renders again with that camera.
        if (SameWorld(job.Scene, _scene) && (_scene.IsNew || job.Kind == TweakRun.AutoCamera))
        {
            bool movedOn = (_scene with { Camera = null }) != (job.Scene with { Camera = null });
            _scene = _scene with { Camera = sidecar.Camera };
            ShowScene();
            if (movedOn)
                Submit();
        }
        bool current = (job.Scene with { Camera = null }) == (_scene with { Camera = null }) && (job.Scene.Camera is null || job.Scene.Camera == _scene.Camera);
        if (result.Image is { } image)
        {
            _preview.Show(PreviewPanel.ToBitmap(image));
            _preview.Caption = $"{image.Width}×{image.Height} · {image.SamplesPerPixel} samples · {result.ImageRole}{(current ? string.Empty : " · an earlier scene")}";
        }
        LastPreview = sidecar;
        _command.Text = job.CommandLine;
        ShowCamera();
        string Names(IReadOnlyList<PreviewStage> stages) => stages.Count == 0 ? "nothing" : string.Join(", ", stages.Select(s => JsonName(s)));
        string warnings = sidecar.Warnings.Count > 0 ? " · ⚠ " + string.Join(" ", sidecar.Warnings) : string.Empty;
        Status(string.Create(CultureInfo.InvariantCulture,
            $"{Describe(job.Kind)} in {result.Seconds:0.00} s · built {Names(sidecar.Stages.Built)} · reused {Names(sidecar.Stages.Reused)} · " +
            $"sun {sidecar.Sun.ElevationDegrees:0.0}° at {sidecar.Sun.SolarTime} solar, {sidecar.Sun.Date:yyyy-MM-dd}, {sidecar.Place.Name}{warnings}"));
    }

    private void ShowFinal(TweakResult result, Sidecar sidecar)
    {
        string inside = string.Empty;
        if (_estimate is { } kept && kept.Scene == result.Job.Scene && kept.Estimate.Total is { } total)
        {
            bool within = result.Seconds >= total.Low && result.Seconds <= total.High;
            inside = $"; the estimate said {total}: {(within ? "inside" : "outside")} it";
        }
        Say(_finalText, string.Create(CultureInfo.InvariantCulture,
            $"Final: {result.Job.OutPath} ({sidecar.Image.Width}×{sidecar.Image.Height}, {sidecar.Image.SamplesPerPixel} spp) in {TimeRange.Describe(result.Seconds)}{inside}. " +
            $"Its EXR, sidecar and scene files are beside it."));
        _controls.ScrollControlIntoView(_finalText);
        Status($"Final written: {result.Job.OutPath}");
    }

    // ---- Files ----

    private void OpenScene()
    {
        using var dialog = new OpenFileDialog { Filter = "Vista recipe (*.recipe.json; *.json)|*.recipe.json;*.json", Title = "Open a Vista scene" };
        if (_saved is { } saved)
            dialog.InitialDirectory = saved.Folder;
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            LoadScene(dialog.FileName);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, e.Message, "Vista: cannot open the scene", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Save(bool as_)
    {
        if (!as_ && _saved is { } saved)
        {
            SaveScene(saved.Recipe);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "Vista scene (*.recipe.json)|*.recipe.json",
            FileName = (_saved?.Name ?? Slug(_scene.Recipe.Name ?? "scene")) + TweakFiles.RecipeSuffix,
            Title = "Save the scene: its recipe, camera, slider and options files",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SaveScene(dialog.FileName);
    }

    private void BrowseFinal()
    {
        using var dialog = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = Path.GetFileName(_finalPath.Text), Title = "The final's PNG (its EXR and sidecar go beside it)" };
        string? folder = Path.GetDirectoryName(_finalPath.Text);
        if (folder is { Length: > 0 } && Directory.Exists(folder))
            dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _finalPath.Text = dialog.FileName;
    }

    private void CopyCommand()
    {
        if (_command.Text.Length > 0)
            Clipboard.SetText(_command.Text);
    }

    private static void OpenFolder(string folder) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();

    /// <summary>The final's PNG at <paramref name="path"/>, made absolute and given its <c>.png</c>.</summary>
    private static string FinalOut(string path)
    {
        string full = Path.GetFullPath(path);
        return full.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? full : full + ".png";
    }

    /// <summary>The scene files a final writes beside its PNG, named after it, so its sidecar points at files that make it again.</summary>
    private static TweakPaths FinalPaths(string finalOut) =>
        TweakPaths.Of(Path.GetDirectoryName(finalOut)!, Path.GetFileNameWithoutExtension(finalOut));

    private string DefaultFinalPath() => DefaultFinalPath(_saved);

    private string DefaultFinalPath(TweakPaths? saved)
    {
        string folder = saved?.Folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Vista");
        string name = saved?.Name ?? Slug(_scene.Recipe.Name ?? "vista");
        return Path.Combine(folder, $"{name}-final.png");
    }

    // ---- Helpers ----

    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(action);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            // The window is closing: there is nothing left to show the run in.
        }
    }

    private void Status(string text) => _status.Text = text;

    /// <summary>Sets <paramref name="label"/>'s text, and shows it only with some: an empty note takes no room.</summary>
    private static void Say(Label label, string text)
    {
        label.Text = text;
        label.Visible = text.Length > 0;
    }

    private void AddRow(TableLayoutPanel table, Slider slider, string name, Control editor, Action<SliderReading> show, string tip)
    {
        var row = new SliderRow(slider, name, editor, show, tip);
        row.Reset.Click += (_, _) => Apply(_scene.Reset(slider));
        row.AddTo(table, table.RowCount++, _tips);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rows.Add(row);
    }

    private static bool SameWorld(TweakScene a, TweakScene b) =>
        a.Seed == b.Seed && a.Recipe.SetPieces.ToHashSet().SetEquals(b.Recipe.SetPieces);

    private static string Describe(TweakRun kind) => kind switch
    {
        TweakRun.Preview => "Preview",
        TweakRun.AutoCamera => "Auto camera",
        TweakRun.Estimate => "Estimate",
        _ => "Final",
    };

    private static string Describe(EstimateInfo e)
    {
        string GiB(long bytes) => (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GiB";
        string fit = e.Memory.Fits ? (e.Memory.Tight ? "tight" : "fits") : "does not fit";
        string memory = $"video memory {GiB(e.Memory.PlannedPeakBytes)} of {GiB(e.Memory.BudgetBytes)}: {fit}";
        if (e.Total is not { } total || e.Trace is not { } trace || e.Write is not { } write)
            return $"Estimate for {e.Width}×{e.Height} at {e.SamplesPerPixel} spp: none, the render does not fit ({memory}).";
        string denoise = e.Denoise is { } d ? $", denoising {TimeRange.Describe(d.Mid)}" : string.Empty;
        string noise = e.Noise is { } n
            ? string.Create(CultureInfo.InvariantCulture, $"; noise {n.StepsAtSamples:0.00} steps of the 8-bit PNG") +
              (n.SamplesForTarget is int target ? $", {n.SamplesForTargetLow}–{target} spp for {n.TargetSteps:0.##} steps (whole run {n.TotalForTarget})" : string.Empty)
            : string.Empty;
        return $"Estimate for {e.Width}×{e.Height} at {e.SamplesPerPixel} spp: {total}" + Environment.NewLine +
            $"  trace {TimeRange.Describe(trace.Mid)}, files {TimeRange.Describe(write.Mid)}, still to build {TimeRange.Describe(e.Setup.Mid)}{denoise}; {memory}{noise}";
    }

    private static int IndexOfPlace(string name)
    {
        for (int i = 0; i < Places.All.Count; i++)
        {
            if (JsonName(Places.All[i].Place) == name)
                return i;
        }
        return -1;
    }

    private static Place PlaceOf(string name) => IndexOfPlace(name) is int i and >= 0 ? Places.All[i].Place : Place.None;

    private static string JsonName<T>(T value) where T : struct, Enum =>
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// <summary>A set piece's name in words: <c>snowCappedMountains</c> as "snow capped mountains".</summary>
    private static string Words(SetPiece piece) =>
        string.Concat(piece.ToString().Select((c, i) => char.IsUpper(c) && i > 0 ? " " + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static string Metres(double metres) => metres == 1.5
        ? "1.5 m (5 ft)"
        : string.Create(CultureInfo.InvariantCulture, $"{metres} m");

    private static string Slug(string name) =>
        new string([.. name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]).Trim('-') is { Length: > 0 } slug ? slug : "scene";

    private static decimal Clamp(NumericUpDown box, double value) =>
        Math.Clamp((decimal)Math.Round(value, box.DecimalPlaces), box.Minimum, box.Maximum);

    private static void Setup(TrackBar track, TrackScale scale)
    {
        track.Minimum = 0;
        track.Maximum = scale.Last;
    }

    private static ComboBox Combo(IEnumerable<string> items)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        box.Items.AddRange([.. items]);
        return box;
    }

    private static TrackBar Track() => new() { TickStyle = TickStyle.None, AutoSize = false, Height = 26, Width = 170, SmallChange = 1, LargeChange = 10 };

    private static NumericUpDown Number(decimal min, decimal max, decimal step, int decimals) =>
        new() { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals, Width = 80 };

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };

    private static TableLayoutPanel Table(int columns = 5)
    {
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = columns };
        if (columns == 5)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        }
        return table;
    }

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        flow.Controls.AddRange(controls);
        return flow;
    }

    private static Control Stack(params Control[] controls)
    {
        var stack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        stack.Controls.AddRange(controls);
        return stack;
    }

    private static GroupBox Group(string title, Control content)
    {
        var group = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6), MinimumSize = new Size(552, 0) };
        content.Location = group.DisplayRectangle.Location;
        group.Controls.Add(content);
        return group;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
            _settle.Dispose();
        }
        base.Dispose(disposing);
    }
}
