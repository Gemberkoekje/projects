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
public partial class GameController : Node, ICommandSender
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
    private bool notebookIngredients;
    private string patronVisitId = "";
    private DeskDocument document;
    private DayPhase shownPhase;
    private bool tintedLamp;
    private int lettersReadDay;
    private int trayDay;
    private bool summaryOpen;
    private bool weekOpen;
    private bool patronLeaving;
    // The silhouette's fade may or may not be running; null means none (a justified nullable: entity or none).
    private Tween? patronFade;

    /// <summary>Raised when the player leaves the game for the title screen.</summary>
    public event Action LeftForTitle = delegate { };

    /// <summary>Whether each evening is saved to user://save.json (off for screenshots, bots and tests).</summary>
    public bool Autosave { get; set; }

    /// <summary>Whether documents keep full brightness in every phase (the reading-lamp setting).</summary>
    public bool ReadingLamp { get; set; }

    /// <summary>The book open on the desk, or empty.</summary>
    public string OpenBookId => openBookId;

    /// <summary>The game being played.</summary>
    public GameSession Session => session ?? throw new InvalidOperationException("The controller isn't set up yet.");

    /// <summary>The scene the controller lights and moves.</summary>
    public WorldView World => Screen.World;

    /// <summary>Whether a summary or question is covering the screen.</summary>
    public bool Modal => summaryOpen || weekOpen || Screen.Confirm.Visible;

    /// <summary>Whether the week summary is showing.</summary>
    public bool WeekSummaryShowing => weekOpen;

    private Parts Screen => parts ?? throw new InvalidOperationException("The controller isn't set up yet.");

    /// <summary>Wires the controller to the session and the parts of the screen it drives.</summary>
    /// <param name="gameSession">The game.</param>
    /// <param name="worldView">The painted scene.</param>
    /// <param name="artLayout">The layout, for the document tint by phase.</param>
    /// <param name="lineToast">Where gentle lines appear.</param>
    /// <param name="confirmPanel">The close-early question.</param>
    /// <param name="summaryPanel">The day summary.</param>
    /// <param name="weekPanel">The week summary.</param>
    public void Setup(GameSession gameSession, WorldView worldView, ArtLayout artLayout, LineToast lineToast, ConfirmPanel confirmPanel, DaySummaryPanel summaryPanel, WeekSummaryPanel weekPanel)
    {
        session = gameSession;
        parts = new Parts(worldView, artLayout, lineToast, confirmPanel, summaryPanel, weekPanel);
        worldView.Camera.ViewChanged += OnViewChanged;
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
            if (after.Transcript.Count > 0 && after.Transcript[^1].Kind == SpeechKind.Narration)
            {
                Screen.Toast.Say(after.LastLine);
            }
            else
            {
                Screen.Toast.Speak(after.PatronName, after.LastLine);
            }
        }

        var ended = result.Events.OfType<DayEnded>().LastOrDefault();
        if (ended is not null && Autosave)
        {
            SaveStore.Save(Session);
        }

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
    public void RingBell()
    {
        ReleaseFocus();
        Send(new RingBell());
    }

    /// <summary>Takes keyboard focus away from any note being written, so the keys go back to the game.</summary>
    public void ReleaseFocus() => GetViewport()?.GuiReleaseFocus();

    /// <summary>What the desk looks like now, to carry over when the views are rebuilt.</summary>
    /// <returns>The state.</returns>
    public DeskState CaptureDesk() => new(Screen.World.Camera.LookingUp, document, openBookId, notebookPatronId, [.. shelved], trayDay, lettersReadDay, notebookIngredients);

    /// <summary>Puts the desk back as it was before the views were rebuilt.</summary>
    /// <param name="state">The state captured from the old controller.</param>
    public void RestoreDesk(DeskState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        document = state.Document;
        openBookId = state.OpenBookId;
        notebookPatronId = state.NotebookPatronId;
        notebookIngredients = state.NotebookIngredients;
        trayDay = state.TrayDay;
        lettersReadDay = state.LettersReadDay;
        shelved.Clear();
        shelved.UnionWith(state.Shelved);
        if (state.LookingUp)
        {
            Screen.World.Camera.LookUp(immediate: true);
        }
        else
        {
            Screen.World.Camera.LookDown(immediate: true);
        }

        Refresh();
    }

    /// <summary>Closes the library, asking first if patrons are still to come.</summary>
    public void RequestClose()
    {
        ReleaseFocus();
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
        ReleaseFocus();
        document = document == which ? DeskDocument.None : which;
        MarkRead(document);
        Refresh();
    }

    /// <summary>Opens a document on the desk.</summary>
    /// <param name="which">The document.</param>
    public void Open(DeskDocument which)
    {
        ReleaseFocus();
        document = which;
        MarkRead(document);
        Refresh();
    }

    /// <summary>Pulls a book's card: the book lands on the desk.</summary>
    /// <param name="bookId">The book.</param>
    public void OpenBook(string bookId)
    {
        ReleaseFocus();
        openBookId = bookId;
        document = DeskDocument.Book;
        Refresh();
    }

    /// <summary>Turns the notebook to a patron's page.</summary>
    /// <param name="patronId">The patron.</param>
    public void ShowNotebookPage(string patronId)
    {
        ReleaseFocus();
        notebookPatronId = patronId;
        notebookIngredients = false;
        document = DeskDocument.Notebook;
        Refresh();
    }

    /// <summary>Turns the notebook to the ingredient log.</summary>
    public void ShowIngredientLog()
    {
        ReleaseFocus();
        notebookIngredients = true;
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
            notebookIngredients = false;
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
            Screen.World.Camera.LookingUp,
            summaryOpen || weekOpen,
            lettersReadDay == hud.Day,
            projector.IngredientLog(),
            notebookIngredients);
        UpdateWorld(hud, visit);
        foreach (var view in views)
        {
            view.Render(snapshot);
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        // The world outlives this game's controller (the title, a text-size rebuild): let go of its camera.
        parts?.World.Camera.ViewChanged -= OnViewChanged;
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
        else if (@event.IsActionPressed("notebook") || @event.IsActionPressed("catalogue"))
        {
            // The key asks for the document, so the camera goes down to the desk where it lies.
            var which = @event.IsActionPressed("notebook") ? DeskDocument.Notebook : DeskDocument.Catalogue;
            if (Screen.World.Camera.LookingUp)
            {
                Screen.World.Camera.LookDown();
                Open(which);
            }
            else
            {
                Toggle(which);
            }
        }
        else
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>Answers an open yes-or-no question (closing early) with no, as Esc does.</summary>
    /// <returns>Whether a question was open.</returns>
    public bool DismissQuestion()
    {
        if (!Screen.Confirm.Visible)
        {
            return false;
        }

        Screen.Confirm.Visible = false;
        Refresh();
        return true;
    }

    /// <summary>Puts the day summary away, as its button does; after the last day, the week summary follows.</summary>
    public void DismissSummary()
    {
        if (summaryOpen)
        {
            Screen.Summary.Visible = false;
            CloseSummary();
        }
    }

    /// <summary>Leaves the week summary for the title screen.</summary>
    public void LeaveForTitle()
    {
        weekOpen = false;
        Screen.Week.Visible = false;
        LeftForTitle();
    }

    /// <summary>Shows the week summary now, for a game restored after the week ended.</summary>
    public void ShowWeekSummary()
    {
        weekOpen = true;
        Screen.Week.Open(Session.Views.WeekSummary(), LeaveForTitle);
        Refresh();
    }

    private void OnViewChanged(bool lookingUp)
    {
        ReleaseFocus();
        Refresh();
    }

    private void MarkRead(DeskDocument opened)
    {
        if (opened == DeskDocument.Letters)
        {
            lettersReadDay = Session.State.Day;
        }
    }

    private void CloseSummary()
    {
        summaryOpen = false;
        if (Session.State.Status == DayStatus.WeekOver)
        {
            ShowWeekSummary();
            return;
        }

        Refresh();
    }

    private void UpdateWorld(HudView hud, VisitView visit)
    {
        var phase = summaryOpen || weekOpen ? DayPhase.Night : hud.Phase;
        if ((phase != shownPhase || ReadingLamp != tintedLamp) && phase != DayPhase.None)
        {
            var immediate = shownPhase == DayPhase.None;
            if (phase != shownPhase)
            {
                Screen.World.Lights.SetPhase(phase, immediate);
            }

            // Turning the reading lamp on or off is a switch, not a change of light: it applies at once,
            // even behind the settings menu while the game is paused.
            immediate |= phase == shownPhase && ReadingLamp != tintedLamp;
            tintedLamp = ReadingLamp;
            if (Screen.Layout.Phases.TryGetValue(phase, out var light))
            {
                var tint = ReadingLamp ? Colors.White : light.DocumentTint;
                foreach (var item in documents)
                {
                    if (immediate)
                    {
                        item.Modulate = tint;
                    }
                    else
                    {
                        CreateTween().TweenProperty(item, "modulate", tint, Screen.Layout.BlendSeconds);
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

    private sealed record Parts(WorldView World, ArtLayout Layout, LineToast Toast, ConfirmPanel Confirm, DaySummaryPanel Summary, WeekSummaryPanel Week);
}
