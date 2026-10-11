using Curator.Core.Commands;
using Curator.Presentation.Game;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Desk;

/// <summary>The bell and the closing sign on the counter, seen from both views (Space rings the bell too).</summary>
public partial class CounterProps : Control, IGameView
{
    private readonly Button bell = new();
    private readonly Button close = new();
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the counter first.");

    /// <summary>Connects the props to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    public void Bind(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(1420, 900);
        Size = new Vector2(480, 72);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, Size = Size };
        row.AddThemeConstantOverride("separation", 14);
        Style(bell, UiText.Bell, new Color("b08d3c"), new Color("5e4614"));
        bell.Pressed += () => Game.RingBell();
        Style(close, UiText.CloseLibrary, new Color("6b4a2e"), new Color("2e1d10"));
        close.AddThemeColorOverride("font_color", UiTheme.SpeechInk);
        close.AddThemeColorOverride("font_hover_color", Colors.White);
        close.Pressed += () => Game.RequestClose();
        row.AddChild(bell);
        row.AddChild(close);
        AddChild(row);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var hud = snapshot.Hud;
        bell.Disabled = !hud.CanRingBell;
        bell.TooltipText = hud.CanRingBell ? "" : Game.WhyNot(new RingBell());
        close.Disabled = !hud.CanClose;
        close.TooltipText = hud.CanClose ? "" : Game.WhyNot(new CloseForTheDay());
    }

    private static void Style(Button button, string text, Color back, Color border)
    {
        button.Text = text;
        button.FocusMode = FocusModeEnum.None;
        button.CustomMinimumSize = new Vector2(0, 56);
        button.AddThemeFontSizeOverride("font_size", 22);
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var fill = state switch
            {
                "hover" => back.Lightened(0.15f),
                "pressed" => back.Darkened(0.15f),
                "disabled" => new Color(back.Darkened(0.3f), 0.6f),
                _ => back,
            };
            var box = UiTheme.Box(fill, border, 2, 8, new Vector4(18, 6, 18, 6));
            box.ShadowColor = new Color(0f, 0f, 0f, 0.45f);
            box.ShadowSize = 4;
            box.ShadowOffset = new Vector2(2, 3);
            button.AddThemeStyleboxOverride(state, box);
        }
    }
}
