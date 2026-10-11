using Curator.Core.Commands;
using Curator.Core.Events;
using Curator.Core.Game;
using Curator.Core.Projections;
using Curator.Presentation.Art;
using Curator.Presentation.Ui;
using Curator.Presentation.World;
using Godot;

namespace Curator.Presentation.Game;

/// <summary>
/// Owns the <see cref="GameSession"/>, sends it commands, and pushes fresh projections to every view
/// after each one (BUILD_BRIEF §7.1). Views never read each other; they call back in here.
/// Holds only presentation state: which document is open, which returned books were shelved.
/// </summary>
public partial class GameController : Node
{
    private const float PatronFadeSeconds = 0.6f;
    private const float PatronLeavesAfter = 2.5f;

    private readonly List<IGameView> views = [];
    private readonly HashSet<string> shelved = new(StringComparer.Ordinal);
    // Null until Setup (a justified nullable: no game yet); nothing calls in before then.
    private GameSession? session;
    // Null until Setup (a justified nullable: no screen to drive yet); nothing calls in before then.
    private Parts? parts;
    private readonly List<CanvasItem> documents = [];
    private string openBookId = "";
    private string notebookPatronId = "";
    private string patronVisitId = "";
    private DeskDocument document;
    private DayPhase shownPhase;
    private int trayDay;
    private bool summaryOpen;
    private bool patronLeaving;
    // The silhouette's fade may or may not be running; null means none (a justified nullable: entity or none).
    private Tween? patronFade;

    /// <summary>The game being played.</summary>
    public GameSession Session => session ?? throw new InvalidOperationException("The controller isn't set up yet.");

    /// <summary>The scene the controller lights and moves.</summary>
    public WorldView World => Screen.World;

    /// <summary>Whether a summary or question is covering the screen.</summary>
    public bool Modal => summaryOpen || Screen.Confirm.Visible;

    private Parts Screen => parts ?? throw new InvalidOperationException("The controller isn't set up yet.");

    /// <summary>Wires the controller to the session and the parts of the screen it drives.</summary>
    /// <param name="gameSession">The game.</param>
    /// <param name="worldView">The painted scene.</param>
    /// <param name="artLayout">The layout, for the document tint by phase.</param>
    /// <param name="lineToast">Where gentle lines appear.</param>
    /// <param name="confirmPanel">The close-early question.</param>
    /// <param name="summaryPanel">The day summary.</param>
    public void Setup(GameSession gameSession, WorldView worldView, ArtLayout artLayout, LineToast lineToast, ConfirmPanel confirmPanel, DaySummaryPanel summaryPanel)
    {
        session = gameSession;
        parts = new Parts(worldView, artLayout, lineToast, confirmPanel, summaryPanel);
        worldView.Camera.ViewChanged += _ => Refresh();
    }

    /// <summary>Adds a view to push projections to.</summary>
    /// <param name="view">The view.</param>
    public void AddView(IGameView view) => views.Add(view);

    /// <summary>Adds a layer of documents that takes the phase's document tint.</summary>
    /// <param name="item">The layer's root.</param>
    public void AddDocuments(CanvasItem item) => documents.Add(item);

    /// <summary>Sends a command; a refusal shows its gentle line instead.</summary>
    /// <param name="command">The command.</param>
    /// <returns>What happened.</returns>
    public CommandResult Send(Command command)
    {
        var before = Session.Views.Visit();
        var result = Session.Handle(command);
        if (!result.Accepted)
        {
            Screen.Toast.Say(Session.Views.RefusalLine(result.Rejection));
            Refresh();
            return result;
        }

        if (command is Identify)
        {
            var note = Session.Views.IdentifyNote(result.Events);
            if (note.Length > 0)
            {
                Screen.Toast.Say(note);
            }
        }

        if (result.Events.Any(e => e is VisitStarted))
        {
            Screen.World.Camera.LookUp();
        }

        var after = Session.Views.Visit();
        if (!Screen.World.Camera.LookingUp && after.Present && after.LastLine.Length > 0 && after.LastLine != before.LastLine)
        {
            Screen.Toast.Speak(after.PatronName, after.LastLine);
        }

        var ended = result.Events.OfType<DayEnded>().LastOrDefault();
        if (ended is not null)
        {
            summaryOpen = true;
            Screen.Summary.Open(Session.Views.DaySummary(ended.Day), result.Events.Any(e => e is WeekEnded), CloseSummary);
        }

        Refresh();
        return result;
    }

    /// <summary>Why a command would be refused now, as its gentle line; empty when it's allowed.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The line.</returns>
    public string WhyNot(Command command)
    {
        var reason = Session.Check(command);
        return reason == RejectionReason.None ? "" : Session.Views.RefusalLine(reason);
    }

    /// <summary>Rings the bell for the next patron.</summary>
    public void RingBell() => Send(new RingBell());

    /// <summary>Closes the library, asking first if patrons are still to come.</summary>
    public void RequestClose()
    {
        var hud = Session.Views.Hud();
        if (hud.CanClose && hud.SlotsLeft)
        {
            Screen.Confirm.Ask(hud.CloseEarlyPrompt, () => Send(new CloseForTheDay()));
        }
        else
        {
            Send(new CloseForTheDay());
        }
    }

    /// <summary>Opens a document on the desk, or puts it away if it's already open.</summary>
    /// <param name="which">The document.</param>
    public void Toggle(DeskDocument which)
    {
        document = document == which ? DeskDocument.None : which;
        Refresh();
    }

    /// <summary>Opens a document on the desk.</summary>
    /// <param name="which">The document.</param>
    public void Open(DeskDocument which)
    {
        document = which;
        Refresh();
    }

    /// <summary>Pulls a book's card: the book lands on the desk.</summary>
    /// <param name="bookId">The book.</param>
    public void OpenBook(string bookId)
    {
        openBookId = bookId;
        document = DeskDocument.Book;
        Refresh();
    }

    /// <summary>Turns the notebook to a patron's page.</summary>
    /// <param name="patronId">The patron.</param>
    public void ShowNotebookPage(string patronId)
    {
        notebookPatronId = patronId;
        document = DeskDocument.Notebook;
        Refresh();
    }

    /// <summary>Puts a returned book back on the shelf (presentation only, Q9).</summary>
    /// <param name="loanId">The loan it came back from.</param>
    public void Shelve(string loanId)
    {
        shelved.Add(loanId);
        Refresh();
    }

    /// <summary>Keeps the curator's free notes for a patron. Not an event: the game never reads them.</summary>
    /// <param name="patronId">The patron.</param>
    /// <param name="text">The notes.</param>
    public void SetFreeNotes(string patronId, string text) => Session.Notes.SetFreeNotes(patronId, text);

    /// <summary>Keeps the curator's note on a watched loan.</summary>
    /// <param name="loanId">The loan.</param>
    /// <param name="text">The note.</param>
    public void SetLedgerNote(string loanId, string text) => Session.Notes.SetLedgerNote(loanId, text);

    /// <summary>Builds fresh projections and pushes them to every view.</summary>
    public void Refresh()
    {
        var projector = Session.Views;
        var hud = projector.Hud();
        var visit = projector.Visit();
        if (hud.Day != trayDay)
        {
            shelved.Clear();
            trayDay = hud.Day;
        }

        if (visit.Present && visit.VisitId != patronVisitId && !visit.Decided)
        {
            notebookPatronId = visit.PatronId;
        }

        var index = projector.NotebookIndex();
        if (index.All(e => e.PatronId != notebookPatronId))
        {
            notebookPatronId = index.Count > 0 ? index[^1].PatronId : "";
        }

        var snapshot = new Snapshot(
            hud,
            visit,
            projector.Morning(),
            projector.Catalogue(),
            visit.Present ? projector.Card(visit.PatronId) : null,
            openBookId.Length > 0 ? projector.Book(openBookId) : null,
            index,
            notebookPatronId.Length > 0 ? projector.Notebook(notebookPatronId) : null,
            document == DeskDocument.Book && openBookId.Length == 0 ? DeskDocument.None : document,
            shelved,
            Screen.World.Camera.LookingUp);
        UpdateWorld(hud, visit);
        foreach (var view in views)
        {
            view.Render(snapshot);
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (Modal)
        {
            return;
        }

        if (@event.IsActionPressed("bell"))
        {
            RingBell();
        }
        else if (@event.IsActionPressed("notebook"))
        {
            Toggle(DeskDocument.Notebook);
        }
        else if (@event.IsActionPressed("catalogue"))
        {
            Toggle(DeskDocument.Catalogue);
        }
        else
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    private void CloseSummary()
    {
        summaryOpen = false;
        Refresh();
    }

    private void UpdateWorld(HudView hud, VisitView visit)
    {
        var phase = summaryOpen ? DayPhase.Night : hud.Phase;
        if (phase != shownPhase && phase != DayPhase.None)
        {
            var immediate = shownPhase == DayPhase.None;
            Screen.World.Lights.SetPhase(phase, immediate);
            if (Screen.Layout.Phases.TryGetValue(phase, out var light))
            {
                foreach (var item in documents)
                {
                    if (immediate)
                    {
                        item.Modulate = light.DocumentTint;
                    }
                    else
                    {
                        CreateTween().TweenProperty(item, "modulate", light.DocumentTint, Screen.Layout.BlendSeconds);
                    }
                }
            }

            shownPhase = phase;
        }

        var patron = Screen.World.Patron;
        if (!visit.Present)
        {
            patronVisitId = "";
            patronLeaving = false;
            patronFade?.Kill();
            Screen.World.ShowPatron("");
        }
        else if (visit.VisitId != patronVisitId)
        {
            patronVisitId = visit.VisitId;
            patronLeaving = false;
            Screen.World.ShowPatron(visit.PatronId);
            patron.Modulate = new Color(1f, 1f, 1f, 0f);
            patronFade?.Kill();
            patronFade = CreateTween();
            patronFade.TweenProperty(patron, "modulate:a", 1f, PatronFadeSeconds);
        }

        if (visit.Present && visit.Decided && !patronLeaving)
        {
            patronLeaving = true;
            patronFade?.Kill();
            patronFade = CreateTween();
            patronFade.TweenInterval(PatronLeavesAfter);
            patronFade.TweenProperty(patron, "modulate:a", 0f, PatronFadeSeconds);
        }
    }

    private sealed record Parts(WorldView World, ArtLayout Layout, LineToast Toast, ConfirmPanel Confirm, DaySummaryPanel Summary);
}
