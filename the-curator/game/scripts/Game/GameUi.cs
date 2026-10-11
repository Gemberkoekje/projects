using Curator.Core.Game;
using Curator.Presentation.Art;
using Curator.Presentation.Desk;
using Curator.Presentation.Speech;
using Curator.Presentation.Ui;
using Curator.Presentation.World;
using Godot;

namespace Curator.Presentation.Game;

/// <summary>
/// One game's controller and views, built over the shared world and torn down when the player leaves
/// for the title, or rebuilt when the text size changes (the session carries on untouched).
/// </summary>
public sealed class GameUi
{
    private readonly List<Node> owned = [];

    private GameUi(GameController controller) => Controller = controller;

    /// <summary>The game's controller.</summary>
    public GameController Controller { get; }

    /// <summary>Builds the views for a session and adds them to the scene.</summary>
    /// <param name="parent">Where the controller goes.</param>
    /// <param name="world">The painted scene.</param>
    /// <param name="ui">The UI layer.</param>
    /// <param name="debugLayer">The debug layer.</param>
    /// <param name="layout">The art layout.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="session">The game.</param>
    /// <returns>The built UI.</returns>
    public static GameUi Build(Node parent, WorldView world, CanvasLayer ui, CanvasLayer debugLayer, ArtLayout layout, Theme theme, GameSession session)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(debugLayer);
        var controller = new GameController { Name = "Game", ProcessMode = Node.ProcessModeEnum.Pausable };
        var built = new GameUi(controller);
        var documents = new Control { Name = "Documents", Theme = theme, MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(1920, 1800) };

        // Behind everything on the desk and counter: a click on bare wood takes focus from any note.
        var bareWood = new FocusCatcher { Name = "BareWood", Size = documents.Size };
        bareWood.Clicked += controller.ReleaseFocus;
        documents.AddChild(bareWood);
        var desk = new DeskView { Name = "DeskView" };
        var counter = new CounterProps { Name = "Counter" };
        documents.AddChild(desk);
        documents.AddChild(counter);
        built.Own(world.Desk, documents);

        var speechLayer = new CanvasLayer { Name = "Speech", Layer = 9, FollowViewportEnabled = true };
        var speech = new SpeechView { Name = "SpeechView", Theme = theme };
        speechLayer.AddChild(speech);
        built.Own(ui, speechLayer);
        var hud = new Hud { Name = "Hud", Theme = theme };
        var toast = new LineToast { Name = "Line", Theme = theme };
        var summary = new DaySummaryPanel { Name = "DaySummary", Theme = theme };
        var week = new WeekSummaryPanel { Name = "WeekSummary", Theme = theme };
        var confirm = new ConfirmPanel { Name = "Confirm", Theme = theme };
        foreach (var node in new Node[] { hud, toast, summary, week, confirm })
        {
            built.Own(ui, node);
        }

        var debug = new DebugOverlay { Name = "DebugOverlay", Theme = theme };
        built.Own(debugLayer, debug);
        built.Own(parent, controller);

        controller.Setup(session, world, layout, toast, confirm, summary, week);
        hud.Bind(controller);
        speech.Bind(controller);
        desk.Bind(controller, layout);
        counter.Bind(controller);
        debug.Bind(controller);
        foreach (var view in new IGameView[] { hud, speech, desk, counter, toast, debug })
        {
            controller.AddView(view);
        }

        controller.AddDocuments(documents);
        controller.AddDocuments(speech);
        foreach (var root in new Node[] { documents, speech, debug })
        {
            Callable.From(() => UiTheme.KeepScrollWheel(root)).CallDeferred();
        }

        return built;
    }

    /// <summary>Removes the controller and every view; the world stays.</summary>
    /// <param name="world">The world, whose patron is cleared.</param>
    /// <param name="resetCamera">Whether the camera goes back to the up view (not for a rebuild mid-game).</param>
    public void Destroy(WorldView world, bool resetCamera)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var node in owned)
        {
            node.GetParent()?.RemoveChild(node);
            node.QueueFree();
        }

        owned.Clear();
        world.ShowPatron("");
        if (resetCamera)
        {
            world.Camera.LookUp(immediate: true);
        }
    }

    private void Own(Node parent, Node node)
    {
        parent.AddChild(node);
        owned.Add(node);
    }
}
