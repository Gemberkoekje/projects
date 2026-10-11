using Curator.Core.Content;
using Curator.Presentation.Desk;
using Curator.Presentation.Game;
using Godot;

namespace Curator.Presentation.Checks;

/// <summary>
/// <c>--clicktest</c>: plays the first two days by real mouse clicks and key presses — events pushed at
/// each button's place on screen — then leaves and continues the saved game, checking that every step
/// did what it should. Exit code 0 when all pass. It proves the mouse and keyboard paths work end to
/// end: controls on the layers that follow the camera, Esc pausing, and a note field letting go of the keyboard.
/// </summary>
public partial class ClickTest : Node
{
    private const string TestSave = "user://clicktest-save.json";
    private readonly List<string> failures = [];
    // Unset until Setup (a justified nullable: nothing to test yet).
    private Main? main;

    private Main Host => main ?? throw new InvalidOperationException("Set up the click test first.");

    private GameController Game => Host.Controller;

    /// <summary>Prepares the test.</summary>
    /// <param name="mainNode">The main scene, whose game is played and continued.</param>
    public void Setup(Main mainNode) => main = mainNode;

    /// <inheritdoc />
    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(1920, 1080);
        SaveStore.SavePath = TestSave;
        SaveStore.Delete();
        Game.Autosave = true;
        await Seconds(0.5);
        var views = Game.Session.Views;

        // Esc pauses the game: the bell's key does nothing until it resumes.
        await Tap(Key.Escape);
        Check("Esc pauses the game", GetTree().Paused && FindButton("Resume") is not null);
        await Tap(Key.Space, ' ');
        await Seconds(0.3);
        Check("the bell's key is ignored while paused", !views.Visit().Present);
        await Tap(Key.Escape);
        Check("Esc again resumes", !GetTree().Paused && FindButton("Resume") is null);

        await Click("Ring the bell");
        await Seconds(1.0);
        Check("the bell brings the clerk", views.Visit().Present && Game.World.Camera.LookingUp);

        await Click("Look down at the desk");
        await Seconds(0.7);
        Check("the tab pans down", !Game.World.Camera.LookingUp);

        await Click("Catalogue");
        Check("the drawer opens", FindButton("Back to the drawer") is null && FindVisibleText("Catalogue", "Label"));
        var requested = views.Visit().RequestedBookTitle;
        await Click(requested);
        Check("a card opens its book", FindButton("Back to the drawer") is not null);

        await Click("Identify", prefix: true);
        Check("Identify reads a page", views.Book(views.Visit().RequestedBookId).Pages.Count + views.Book(views.Visit().RequestedBookId).ResistedPageNumbers.Count > 0 || Game.Session.State.Mana == 0);

        await Click("Stamp and lend", index: 0);
        Check("the stamp lends the book", views.Visit().Decided && views.Visit().Decision == DecisionKind.Lent);

        await Click("Note it");
        Check("Note it writes the loan into the notebook", Game.Session.State.Loans.Any(l => l.LedgerNoted));

        // A note field holds the keyboard while it's written in; a click on bare wood lets go of it.
        if (Game.CaptureDesk().Document != DeskDocument.Notebook)
        {
            await Tap(Key.N, 'n');
        }

        Check("N opens the notebook", Game.CaptureDesk().Document == DeskDocument.Notebook);
        var fields = Visible<TextEdit>();
        Check("the page has a free-notes field", fields.Count > 0);
        if (fields.Count > 0)
        {
            var notes = fields[0];
            foreach (var scroll in Ancestors(notes).OfType<ScrollContainer>())
            {
                scroll.EnsureControlVisible(notes);
            }

            await Frames(3);
            await ClickAt(Centre(notes));
            Check("a click into the free notes takes the keyboard", GetViewport().GuiGetFocusOwner() == notes);
        }

        await Tap(Key.C, 'c');
        Check("while writing, C is a letter, not the catalogue", Game.CaptureDesk().Document == DeskDocument.Notebook);
        await ClickBareWood();
        Check("a click on bare wood lets go of the keyboard", GetViewport().GuiGetFocusOwner() is null);
        await Tap(Key.C, 'c');
        Check("then C opens the catalogue", Game.CaptureDesk().Document == DeskDocument.Catalogue);
        await Tap(Key.C, 'c');
        Check("and C again puts it away", Game.CaptureDesk().Document == DeskDocument.None);

        // The wheel scrolls a document; at the document's end it stops there instead of panning the camera.
        await Tap(Key.C, 'c');
        var drawers = Visible<ScrollContainer>().Where(s => Ancestors(s).OfType<CataloguePanel>().Any() && s.GetVScrollBar().Visible).ToList();
        Check("the catalogue drawer scrolls", drawers.Count > 0);
        if (drawers.Count > 0)
        {
            drawers[0].ScrollVertical = 0;
            await Frames(2);
            await Wheel(Centre(drawers[0]), MouseButton.WheelUp);
            await Seconds(0.6);
            Check("the wheel at the top of the drawer doesn't pan the camera", !Game.World.Camera.LookingUp);
        }

        await Tap(Key.C, 'c');
        if (TryFindBareWood(out var bare))
        {
            await Wheel(bare, MouseButton.WheelUp);
        }

        await Seconds(0.7);
        Check("the wheel over bare wood still pans up", Game.World.Camera.LookingUp);
        await Click("Look down at the desk");
        await Seconds(0.7);

        // The notebook's first tab is the ingredient log: what the identified page listed is in it.
        await Tap(Key.N, 'n');
        await Click("Ingredients");
        var logged = Game.Session.Views.IngredientLog();
        Check("the Ingredients tab turns to the log", Game.CaptureDesk().NotebookIngredients && logged.Count > 0
            && Visible<RichTextLabel>().Any(r => r.Text.Contains(logged[0].Name, StringComparison.Ordinal)));
        await Tap(Key.N, 'n');

        await Click("Close for the day");
        await Seconds(0.5);
        Check("closing ends the day", Game.Session.State.Day == 2 && Game.Modal);

        await Click("Next morning");
        await Seconds(0.3);
        Check("the summary gives way to the morning", !Game.Modal);

        // Day 2: three patrons, each asked a question, then lent to, turned away, lent to.
        for (var visit = 0; visit < 3; visit++)
        {
            await Click("Look up at the counter");
            await Seconds(0.7);
            await Click("Ring the bell");
            await Seconds(1.0);
            var at = views.Visit();
            Check($"patron {visit + 1} arrives", at.Present && !at.Decided);

            // The first patron is asked three questions, so the transcript runs past its panel.
            var asks = visit == 0 ? 3 : 1;
            for (var asked = 0; asked < asks && views.Visit().CanAsk; asked++)
            {
                var open = views.Visit().Questions.Where(q => !q.Asked).ToList();
                if (open.Count == 0)
                {
                    break;
                }

                await Click(open[0].Text);
            }

            Check($"patron {visit + 1} answers {asks} question(s)", views.Visit().Conversation.Count == asks || !views.Visit().Present);
            if (visit == 0)
            {
                await Frames(8);
                var transcripts = Visible<ScrollContainer>().Where(s => s.Name == "Transcript").ToList();
                Check("the transcript is on screen", transcripts.Count > 0);
                foreach (var bar in transcripts.Take(1).Select(t => t.GetVScrollBar()))
                {
                    GD.Print($"     transcript: scrolled {bar.Value} of {bar.MaxValue - bar.Page}");
                    Check("the transcript runs past its panel, and the newest answer is in view", bar.MaxValue > bar.Page && bar.Value >= bar.MaxValue - bar.Page - 1);
                }
            }

            await Click("Look down at the desk");
            await Seconds(0.7);
            if (visit == 1 && views.Visit().CanDecline)
            {
                await Click("Hand back the card");
                Check($"patron {visit + 1} is turned away", views.Visit().Decision == DecisionKind.Declined);
            }
            else if (views.Visit().CanLendRequested)
            {
                await Click("Stamp and lend", index: 0);
                Check($"patron {visit + 1} is lent their book", views.Visit().Decided);
            }
            else
            {
                await Click("Hand back the card");
                Check($"patron {visit + 1} is turned away", views.Visit().Decided);
            }
        }

        await Click("Close for the day");
        await Seconds(0.5);
        Check("day 2 closes", Game.Session.State.Day == 3 && Game.Modal);
        await Click("Next morning");
        await Seconds(0.3);
        Check("day 3 begins", !Game.Modal && Game.Session.Views.Hud().Day == 3);

        // A text-size change made from the pause menu's settings comes back to the pause menu.
        await Tap(Key.Escape);
        Host.TrySettings(2, false);
        await Frames(4);
        Check("a text-size change keeps the pause menu open", GetTree().Paused && FindButton("Resume") is not null);
        Host.TrySettings(0, false);
        await Frames(4);
        await Tap(Key.Escape);
        Check("Esc then resumes", !GetTree().Paused && FindButton("Resume") is null);

        // Esc answers the close-early question with "Not yet".
        await Click("Close for the day");
        Check("closing early asks first", Game.Modal);
        await Tap(Key.Escape);
        Check("Esc answers not yet", !Game.Modal && Game.Session.State.Day == 3 && FindButton("Resume") is null);

        // Save and continue: the evening saved day 3's morning; leave mid-visit, continue, and that progress is gone.
        var savedEvents = Game.Session.Events.Count;
        Check("the evening was saved", SaveStore.Exists);
        await Click("Look up at the counter");
        await Seconds(0.7);
        await Click("Ring the bell");
        await Seconds(0.6);
        Check("day 3's first patron arrives", Game.Session.Views.Visit().Present);
        await Press("menu");
        await Click("Back to the title");
        await Seconds(0.3);
        await Click("Continue");
        await Seconds(0.5);
        var resumed = Game.Session;
        Check("Continue resumes day 3's morning, losing the unsaved visit", resumed.State.Day == 3 && !resumed.Views.Visit().Present && resumed.Events.Count == savedEvents);
        SaveStore.Delete();

        if (failures.Count == 0)
        {
            GD.Print("CLICKTEST PASS: days 1 and 2 played by mouse, then saved and continued");
            GetTree().Quit(0);
        }
        else
        {
            GD.PushError($"CLICKTEST FAIL: {string.Join("; ", failures)}");
            GetTree().Quit(1);
        }
    }

    private void Check(string what, bool passed)
    {
        GD.Print($"{(passed ? "ok  " : "FAIL")} {what}");
        if (!passed)
        {
            failures.Add(what);
        }
    }

    private async Task Click(string text, int index = -1, bool prefix = false)
    {
        var button = FindButton(text, index, prefix);
        if (button is null)
        {
            failures.Add($"no visible button '{text}'");
            GD.Print($"FAIL no visible button '{text}'");
            return;
        }

        await ClickAt(Centre(button));
    }

    private static Vector2 Centre(Control control) => control.GetGlobalTransformWithCanvas() * (control.Size / 2f);

    private async Task ClickAt(Vector2 point)
    {
        foreach (var pressed in new[] { true, false })
        {
            var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = point, GlobalPosition = point };
            GetViewport().PushInput(click, true);
            await Frames(2);
        }

        await Frames(3);
    }

    private async Task ClickBareWood()
    {
        if (TryFindBareWood(out var point))
        {
            await ClickAt(point);
        }
    }

    // The first spot on screen where the mouse would be over the bare wood behind the documents.
    private bool TryFindBareWood(out Vector2 point)
    {
        var bare = GetTree().Root.FindChild("BareWood", true, false);
        var size = GetViewport().GetVisibleRect().Size;
        for (var y = 60f; y < size.Y - 60f; y += 40f)
        {
            for (var x = 60f; x < size.X - 60f; x += 40f)
            {
                point = new Vector2(x, y);
                GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
                if (GetViewport().GuiGetHoveredControl() == bare)
                {
                    return true;
                }
            }
        }

        point = default;
        failures.Add("no bare wood to point at");
        GD.Print("FAIL no bare wood to point at");
        return false;
    }

    private async Task Wheel(Vector2 point, MouseButton button)
    {
        foreach (var pressed in new[] { true, false })
        {
            var wheel = new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, Factor = 1f, Position = point, GlobalPosition = point };
            GetViewport().PushInput(wheel, true);
            await Frames(2);
        }
    }

    private async Task Tap(Key key, char letter = '\0')
    {
        foreach (var pressed in new[] { true, false })
        {
            var press = new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = letter, Pressed = pressed };
            GetViewport().PushInput(press);
            await Frames(2);
        }

        await Frames(2);
    }

    private List<T> Visible<T>()
        where T : Control =>
        [.. GetTree().Root.FindChildren("*", typeof(T).Name, true, false).OfType<T>().Where(c => c.IsVisibleInTree())];

    private static IEnumerable<Node> Ancestors(Node node)
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            yield return parent;
        }
    }

    // The visible, enabled button with this text; index picks among several, -1 takes the last.
    private Button? FindButton(string text, int index = -1, bool prefix = false)
    {
        var matches = GetTree().Root.FindChildren("*", "Button", true, false)
            .OfType<Button>()
            .Where(b => b.IsVisibleInTree() && !b.Disabled && (prefix ? b.Text.StartsWith(text, StringComparison.Ordinal) : b.Text == text || b.GetChildren().OfType<Control>().SelectMany(Labels).Any(l => l.Text == text)))
            .ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        return index >= 0 && index < matches.Count ? matches[index] : matches[^1];
    }

    private static IEnumerable<Label> Labels(Control control) =>
        control.FindChildren("*", "Label", true, false).OfType<Label>().Prepend(control as Label).OfType<Label>();

    private bool FindVisibleText(string text, string type) =>
        GetTree().Root.FindChildren("*", type, true, false).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text == text);

    private async Task Press(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        await Frames(2);
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        await Frames(2);
    }

    private async Task Seconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
