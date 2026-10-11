using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Game;
using Curator.Presentation.Art;
using Curator.Presentation.Checks;
using Curator.Presentation.Desk;
using Curator.Presentation.Game;
using Curator.Presentation.Shots;
using Curator.Presentation.Speech;
using Curator.Presentation.Ui;
using Curator.Presentation.World;
using Godot;

namespace Curator.Presentation;

/// <summary>The root of the main scene: builds the world, the UI and the debug layer, then starts the game.</summary>
public partial class Main : Node
{
    /// <inheritdoc />
    public override void _Ready()
    {
        var options = CommandLine.Parse(OS.GetCmdlineUserArgs());
        var layout = ArtLayout.Load();
        var world = new WorldView { Name = "World" };
        world.Build(layout);
        AddChild(world);
        var ui = new CanvasLayer { Name = "Ui", Layer = 10 };
        AddChild(ui);
        AddChild(new CanvasLayer { Name = "Debug", Layer = 20 });
        var theme = UiTheme.Build();

        ContentSet content;
        try
        {
            content = ContentLoader.Load(new GodotContentSource());
        }
        catch (ContentLoadException ex)
        {
            ShowContentErrors(ui, theme, ex);
            return;
        }

        var seed = options.Seed != 0 ? options.Seed : Random.Shared.NextInt64(1, int.MaxValue);
        var session = new GameSession(content, OS.IsDebugBuild());
        session.Handle(new NewGame(seed));

        var controller = new GameController { Name = "Game" };
        var documents = new Control { Name = "Documents", Theme = theme, MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(1920, 1800) };
        var desk = new DeskView { Name = "DeskView" };
        var counter = new CounterProps { Name = "Counter" };
        documents.AddChild(desk);
        documents.AddChild(counter);
        world.Desk.AddChild(documents);

        var speechLayer = new CanvasLayer { Name = "Speech", Layer = 9, FollowViewportEnabled = true };
        var speech = new SpeechView { Name = "SpeechView", Theme = theme };
        speechLayer.AddChild(speech);
        ui.AddChild(speechLayer);
        var hud = new Hud { Name = "Hud", Theme = theme };
        var toast = new LineToast { Name = "Line", Theme = theme };
        var summary = new DaySummaryPanel { Name = "DaySummary", Theme = theme };
        var confirm = new ConfirmPanel { Name = "Confirm", Theme = theme };
        ui.AddChild(hud);
        ui.AddChild(toast);
        ui.AddChild(summary);
        ui.AddChild(confirm);

        AddChild(controller);
        controller.Setup(session, world, layout, toast, confirm, summary);
        hud.Bind(controller);
        speech.Bind(controller);
        desk.Bind(controller, layout);
        counter.Bind(controller);
        controller.AddView(hud);
        controller.AddView(speech);
        controller.AddView(desk);
        controller.AddView(counter);
        controller.AddView(toast);
        controller.AddDocuments(documents);
        controller.AddDocuments(speech);
        controller.Refresh();

        if (options.ClickTest)
        {
            var test = new ClickTest { Name = "ClickTest" };
            test.Setup(controller);
            AddChild(test);
        }
        else if (options.ShotsDir.Length > 0)
        {
            var shots = new ShotRunner { Name = "Shots" };
            shots.Setup(ResolveOutput(options.ShotsDir), controller);
            AddChild(shots);
        }
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

    private static void ShowContentErrors(CanvasLayer ui, Theme theme, ContentLoadException ex)
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
