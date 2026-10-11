using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Game;
using Curator.Presentation.Art;
using Curator.Presentation.Checks;
using Curator.Presentation.Game;
using Curator.Presentation.Shots;
using Curator.Presentation.Ui;
using Curator.Presentation.World;
using Godot;

namespace Curator.Presentation;

/// <summary>
/// The root of the main scene: builds the world, the UI and the debug layer, shows the title, and
/// starts or continues games. Command-line modes skip the title (BUILD_BRIEF §7.11).
/// </summary>
public partial class Main : Node
{
    private readonly TitleScreen title = new() { Name = "Title" };
    private readonly PauseMenu pause = new() { Name = "Pause" };
    private readonly SettingsPanel settingsPanel = new() { Name = "Settings" };
    private readonly WorldView world = new() { Name = "World" };
    private readonly CanvasLayer ui = new() { Name = "Ui", Layer = 10 };
    private readonly CanvasLayer debugLayer = new() { Name = "Debug", Layer = 20 };
    private readonly CanvasLayer menus = new() { Name = "Menus", Layer = 30 };
    private CommandLine options = CommandLine.Parse([]);
    private ArtLayout layout = ArtLayout.Empty;
    private GameSettings settings = new();
    private Theme theme = new();
    // Null when the content has errors (a justified nullable: no game can start).
    private ContentSet? content;
    // Null on the title screen (a justified nullable: no game is running).
    private GameUi? game;

    /// <summary>The running game's controller.</summary>
    public GameController Controller => game?.Controller ?? throw new InvalidOperationException("No game is running.");

    /// <inheritdoc />
    public override void _Ready()
    {
        ErrorLog.Install();
        options = CommandLine.Parse(OS.GetCmdlineUserArgs());

        // Test and screenshot runs start from the default settings, whatever this machine has saved.
        var checking = options.Autoplay.Length > 0 || options.ClickTest || options.ShotsDir.Length > 0;
        settings = checking ? new GameSettings() : GameSettings.Load();
        settings.Apply();
        layout = ArtLayout.Load();
        UiTheme.UsePaperColors(layout.PaperColors);

        // Esc pauses (§7.12): the game's nodes stop while the menus, and the runners, carry on.
        ProcessMode = ProcessModeEnum.Always;
        foreach (var paused in new Node[] { world, ui, debugLayer })
        {
            paused.ProcessMode = ProcessModeEnum.Pausable;
        }
        world.Build(layout);
        AddChild(world);
        AddChild(ui);
        AddChild(debugLayer);
        AddChild(menus);
        theme = UiTheme.Build();
        foreach (var control in new Control[] { title, pause, settingsPanel })
        {
            control.Theme = theme;
            menus.AddChild(control);
        }

        title.Visible = false;
        title.NewGame += () => StartNew(options.Seed, autosave: true);
        title.Continue += ContinueSaved;
        title.Settings += () => settingsPanel.Open(settings);
        pause.Settings += () => settingsPanel.Open(settings);
        pause.LeaveForTitle += ShowTitle;
        settingsPanel.Changed += SettingsChanged;
        pause.VisibilityChanged += UpdatePaused;
        settingsPanel.VisibilityChanged += UpdatePaused;

        try
        {
            content = ContentLoader.Load(new GodotContentSource());
        }
        catch (ContentLoadException ex)
        {
            ShowContentErrors(ex);
            if (options.Autoplay.Length > 0 || options.ClickTest || options.ShotsDir.Length > 0)
            {
                GetTree().Quit(1);
            }

            return;
        }

        if (options.Autoplay.Length > 0)
        {
            StartNew(options.Seed == 0 ? 1 : options.Seed, autosave: false);
            var autoplay = new AutoplayRunner { Name = "Autoplay" };
            autoplay.Setup(Controller, options.Autoplay, options.Days);
            AddChild(autoplay);
        }
        else if (options.ClickTest)
        {
            StartNew(options.Seed == 0 ? 1 : options.Seed, autosave: false);
            var test = new ClickTest { Name = "ClickTest" };
            test.Setup(this);
            AddChild(test);
        }
        else if (options.ShotsDir.Length > 0)
        {
            ShowTitle();
            var shots = new ShotRunner { Name = "Shots" };
            shots.Setup(ResolveOutput(options.ShotsDir), this, options.Seed == 0 ? 1 : options.Seed);
            AddChild(shots);
        }
        else if (options.NewGame)
        {
            StartNew(options.Seed, autosave: true);
        }
        else
        {
            ShowTitle();
        }
    }

    /// <summary>Starts a new game.</summary>
    /// <param name="seed">The seed; 0 picks one.</param>
    /// <param name="autosave">Whether evenings are saved.</param>
    public void StartNew(long seed, bool autosave)
    {
        var session = new GameSession(content ?? throw new InvalidOperationException("No content."), OS.IsDebugBuild());
        session.Handle(new NewGame(seed != 0 ? seed : Random.Shared.NextInt64(1, int.MaxValue)));
        Begin(session, autosave);
    }

    /// <summary>Changes the settings for this run only, without saving them (screenshots use this).</summary>
    /// <param name="textSize">The text size, an index into <see cref="GameSettings.TextScales"/>.</param>
    /// <param name="readingLamp">Whether the reading lamp is on.</param>
    public void TrySettings(int textSize, bool readingLamp)
    {
        var textSizeChanged = settings.TextSize != textSize;
        settings.TextSize = textSize;
        settings.ReadingLamp = readingLamp;
        settings.Apply();
        SettingsChanged(textSizeChanged);
    }

    /// <summary>Shows the settings panel, as the menus do.</summary>
    public void OpenSettings() => settingsPanel.Open(settings);

    /// <summary>Shows the title screen, ending any game.</summary>
    public void ShowTitle()
    {
        game?.Destroy(world, resetCamera: true);
        game = null;
        GetTree().Paused = false;
        world.Lights.SetPhase(DayPhase.Morning, immediate: true);
        if (content is null)
        {
            title.Open(false, "");
            return;
        }

        var canContinue = SaveStore.TryRestore(content, OS.IsDebugBuild(), out _, out var note);
        title.Open(canContinue, SaveStore.Exists ? note : "");
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (!@event.IsActionPressed("menu") || game is null)
        {
            return;
        }

        if (settingsPanel.Visible)
        {
            settingsPanel.Visible = false;
        }
        else if (!game.Controller.Modal || pause.Visible)
        {
            game.Controller.ReleaseFocus();
            pause.Visible = !pause.Visible;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        UiTheme.Release();
        ArtLibrary.Release();
    }

    /// <summary>
    /// Resolves an output folder from the command line. Godot runs inside game/, so relative paths
    /// are taken from the folder above it (the-curator/), where out/ lives.
    /// </summary>
    /// <param name="path">The path as given.</param>
    /// <returns>An absolute path.</returns>
    public static string ResolveOutput(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal))
        {
            return ProjectSettings.GlobalizePath(path);
        }

        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", path));
    }

    private void ContinueSaved()
    {
        if (content is not null && SaveStore.TryRestore(content, OS.IsDebugBuild(), out var session, out _))
        {
            Begin(session, autosave: true);
        }
    }

    private void UpdatePaused() => GetTree().Paused = game is not null && (pause.Visible || settingsPanel.Visible);

    private void Begin(GameSession session, bool autosave)
    {
        game?.Destroy(world, resetCamera: true);
        title.Visible = false;
        pause.Visible = false;
        game = GameUi.Build(this, world, ui, debugLayer, layout, theme, session);
        game.Controller.Autosave = autosave;
        game.Controller.ReadingLamp = settings.ReadingLamp;
        game.Controller.LeftForTitle += ShowTitle;
        game.Controller.Refresh();
        if (session.State.Status == DayStatus.WeekOver)
        {
            game.Controller.ShowWeekSummary();
        }
    }

    private void SettingsChanged(bool textSizeChanged)
    {
        if (textSizeChanged)
        {
            UiTheme.ScaleFonts(theme);
        }

        if (game is null)
        {
            return;
        }

        if (textSizeChanged)
        {
            // Text sizes are baked into each control as it's built, so the views are rebuilt; the game,
            // the camera and the desk carry on as they were.
            var session = game.Controller.Session;
            var autosave = game.Controller.Autosave;
            var desk = game.Controller.CaptureDesk();
            game.Destroy(world, resetCamera: false);
            game = null;
            Begin(session, autosave);
            Controller.RestoreDesk(desk);
            UpdatePaused();
            return;
        }

        game.Controller.ReadingLamp = settings.ReadingLamp;
        game.Controller.Refresh();
    }

    private void ShowContentErrors(ContentLoadException ex)
    {
        foreach (var problem in ex.Problems)
        {
            GD.PushError($"{problem.File} {problem.JsonPath}: {problem.Message}");
        }

        var panel = new PanelContainer { Theme = theme, Position = new Vector2(160, 120), Size = new Vector2(1600, 840) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Sheet(UiTheme.Paper, 30));
        var text = UiTheme.Para(
            $"The content has errors, so the game can't start:\n\n{string.Join("\n", ex.Problems.Take(30).Select(p => $"{p.File} {p.JsonPath}: {p.Message}"))}",
            UiTheme.Print,
            20,
            UiTheme.Ink);
        panel.AddChild(text);
        ui.AddChild(panel);
    }
}
