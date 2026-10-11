using System.Globalization;
using Curator.Core.Commands;
using Curator.Presentation.Game;
using Godot;

namespace Curator.Presentation.Ui;

/// <summary>
/// F1 in debug builds (BUILD_BRIEF §7.10): the visit's hidden state, reputation, pending outcomes with
/// their surface days and channels, flags, the seed and the last 20 events. Cheats go through
/// Debug… commands, so they're logged: F2 +5 mana, F3 ends the day, F4 reveals the open book.
/// </summary>
public partial class DebugOverlay : Control, IGameView
{
    private const int CheatMana = 5;
    private readonly Label text = UiTheme.Para("", UiTheme.Print, 16, new Color("e8f0ff"));
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the debug overlay first.");

    /// <summary>Connects the overlay to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    public void Bind(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(1180, 130);
        Size = new Vector2(720, 930);
        var panel = new PanelContainer { Size = Size, CustomMinimumSize = Size, MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.03f, 0.05f, 0.1f, 0.88f), new Color(0.5f, 0.7f, 1f, 0.5f), 1, 6, new Vector4(16, 12, 16, 12)));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Ignore };
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(text);
        panel.AddChild(scroll);
        AddChild(panel);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (!Game.Session.DebugEnabled || Game.Modal)
        {
            return;
        }

        if (@event.IsActionPressed("debug_overlay"))
        {
            Visible = !Visible;
            Game.Refresh();
        }
        else if (@event.IsActionPressed("cheat_mana"))
        {
            Game.Send(new DebugAddMana(CheatMana));
        }
        else if (@event.IsActionPressed("cheat_end_day"))
        {
            Game.Send(new DebugEndDay());
        }
        else if (@event.IsActionPressed("cheat_reveal") && Game.OpenBookId.Length > 0)
        {
            Game.Send(new DebugRevealBook(Game.OpenBookId));
        }
        else
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        if (!Visible)
        {
            return;
        }

        var debug = Game.Session.Views.Debug();
        var culture = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            string.Create(culture, $"DEBUG (F1)   seed {debug.Seed}   day {debug.Day}   {debug.Phase}"),
            string.Create(culture, $"reputation {debug.Reputation} ({debug.Band})"),
            "F2 +5 mana   F3 end the day   F4 reveal the open book",
            "",
            debug.VisitId.Length > 0
                ? string.Create(culture, $"visit {debug.VisitId}\n  goal {debug.Goal}\n  temptations {debug.Temptations}\n  trust {debug.Trust}   patience {debug.Patience}")
                : "no visit",
            "",
            "outcomes:",
        };
        lines.AddRange(debug.Outcomes.Select(o => string.Create(culture, $"  {(o.Surfaced ? "heard" : "pending")} day {o.SurfaceDay} {o.Channel}: {o.PatronId} {o.BookId} {o.Category}/{o.Cause} [{o.Key}]")));
        lines.Add("");
        lines.Add($"flags: {string.Join(", ", debug.Flags)}");
        lines.Add("");
        lines.Add("last events:");
        lines.AddRange(debug.RecentEvents.Select(e => $"  {e}"));
        text.Text = string.Join("\n", lines);
    }
}
