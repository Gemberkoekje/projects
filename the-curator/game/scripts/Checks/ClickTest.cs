using Curator.Core.Content;
using Curator.Presentation.Game;
using Godot;

namespace Curator.Presentation.Checks;

/// <summary>
/// <c>--clicktest</c>: plays the first two days by real mouse clicks — events pushed at each button's place
/// on screen — and checks that every step did what it should. Exit code 0 when all pass. It proves
/// the mouse path works end to end, including controls on the layers that follow the camera.
/// </summary>
public partial class ClickTest : Node
{
    // Unset until Setup (a justified nullable: nothing to test yet).
    private GameController? controller;
    private readonly List<string> failures = [];

    private GameController Game => controller ?? throw new InvalidOperationException("Set up the click test first.");

    /// <summary>Prepares the test.</summary>
    /// <param name="gameController">The game.</param>
    public void Setup(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(1920, 1080);
        await Seconds(0.5);
        var views = Game.Session.Views;

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

        if (failures.Count == 0)
        {
            GD.Print("CLICKTEST PASS: days 1 and 2 played by mouse");
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

        var center = button.GetGlobalTransformWithCanvas() * (button.Size / 2f);
        foreach (var pressed in new[] { true, false })
        {
            var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = center, GlobalPosition = center };
            GetViewport().PushInput(click, true);
            await Frames(2);
        }

        await Frames(3);
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

    private async Task Seconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
