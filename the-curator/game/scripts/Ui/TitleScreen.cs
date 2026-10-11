using Godot;

namespace Curator.Presentation.Ui;

/// <summary>The title (BUILD_BRIEF §7.12): New game, Continue, Settings, Quit, over the library at morning.</summary>
public partial class TitleScreen : Control
{
    private readonly Button continueButton = new();
    private readonly Label note = UiTheme.Para("", UiTheme.PrintItalic, 20, UiTheme.SpeechInk);

    /// <summary>Raised for New game.</summary>
    public event Action NewGame = delegate { };

    /// <summary>Raised for Continue.</summary>
    public event Action Continue = delegate { };

    /// <summary>Raised for Settings.</summary>
    public event Action Settings = delegate { };

    /// <inheritdoc />
    public override void _Ready()
    {
        Size = new Vector2(1920, 1080);
        var dim = new ColorRect { Color = new Color(0.03f, 0.02f, 0.02f, 0.55f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        column.AddThemeConstantOverride("separation", 16);
        center.AddChild(column);
        var title = UiTheme.Text("The Curator", UiTheme.Print, 104, UiTheme.SpeechInk);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.6f));
        title.AddThemeConstantOverride("shadow_offset_y", 4);
        column.AddChild(title);
        var subtitle = UiTheme.Text("A week at the library on Lantern Street", UiTheme.PrintItalic, 28, UiTheme.CuratorInk);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(subtitle);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 30) });
        column.AddChild(Big(UiTheme.Button("New game", () => NewGame())));
        continueButton.Text = "Continue";
        continueButton.FocusMode = FocusModeEnum.None;
        continueButton.Pressed += () => Continue();
        column.AddChild(Big(continueButton));
        column.AddChild(Big(UiTheme.Button("Settings", () => Settings())));
        column.AddChild(Big(UiTheme.Button("Quit", () => GetTree().Quit())));
        note.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(note);
    }

    /// <summary>Shows the title.</summary>
    /// <param name="canContinue">Whether there's a game to continue.</param>
    /// <param name="saveNote">A note about the save, or empty.</param>
    public void Open(bool canContinue, string saveNote)
    {
        continueButton.Disabled = !canContinue;
        note.Text = saveNote;
        Visible = true;
    }

    private static Button Big(Button button)
    {
        button.CustomMinimumSize = new Vector2(0, 58);
        button.AddThemeFontSizeOverride("font_size", 28);
        return button;
    }
}
