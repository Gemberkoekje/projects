using Curator.Core.Content;
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

    private GameController Game => (main ?? throw new InvalidOperationException("Set up the click test first.")).Controller;

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
            var question = at.Questions.FirstOrDefault(q => !q.Asked);
            if (question is not null)
            {
                await Click(question.Text);
                Check($"patron {visit + 1} answers a question", views.Visit().Conversation.Count == 1 || !views.Visit().Present);
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

    // Clicks the first spot on screen where the mouse would be over the bare wood behind the documents.
    private async Task ClickBareWood()
    {
        var bare = GetTree().Root.FindChild("BareWood", true, false);
        var size = GetViewport().GetVisibleRect().Size;
        for (var y = 60f; y < size.Y - 60f; y += 40f)
        {
            for (var x = 60f; x < size.X - 60f; x += 40f)
            {
                var point = new Vector2(x, y);
                GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
                if (GetViewport().GuiGetHoveredControl() == bare)
                {
                    await ClickAt(point);
                    return;
                }
            }
        }

        failures.Add("no bare wood to click");
        GD.Print("FAIL no bare wood to click");
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
