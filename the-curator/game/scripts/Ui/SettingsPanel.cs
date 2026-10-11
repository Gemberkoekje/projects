using Curator.Presentation.Game;
using Godot;

namespace Curator.Presentation.Ui;

/// <summary>Settings (BUILD_BRIEF §7.12): fullscreen, text size, reading lamp.</summary>
public partial class SettingsPanel : Control
{
    private readonly CheckButton fullscreen = new() { Text = "Fullscreen", FocusMode = FocusModeEnum.None };
    private readonly OptionButton textSize = new() { FocusMode = FocusModeEnum.None };
    private readonly CheckButton lamp = new() { Text = "Reading lamp: documents stay bright at dusk and night", FocusMode = FocusModeEnum.None };
    private GameSettings settings = new();

    /// <summary>Raised when the settings change, with the text size changed or not.</summary>
    public event Action<bool> Changed = delegate { };

    /// <inheritdoc />
    public override void _Ready()
    {
        Visible = false;
        Size = new Vector2(1920, 1080);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.5f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var sheet = new PanelContainer();
        sheet.AddThemeStyleboxOverride("panel", UiTheme.Sheet(UiTheme.Paper, 32));
        center.AddChild(sheet);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        column.AddThemeConstantOverride("separation", 18);
        sheet.AddChild(column);
        column.AddChild(UiTheme.Text("Settings", UiTheme.Print, 36, UiTheme.Ink));
        foreach (var check in new[] { fullscreen, lamp })
        {
            check.AddThemeColorOverride("font_color", UiTheme.Ink);
            check.AddThemeColorOverride("font_hover_color", UiTheme.Ink);
            check.AddThemeColorOverride("font_pressed_color", UiTheme.Ink);
            check.AddThemeFontSizeOverride("font_size", 24);
        }

        fullscreen.Toggled += on =>
        {
            settings.Fullscreen = on;
            Commit(false);
        };
        column.AddChild(fullscreen);
        var sizeRow = new HBoxContainer();
        sizeRow.AddThemeConstantOverride("separation", 16);
        sizeRow.AddChild(UiTheme.Text("Text size", UiTheme.Print, 24, UiTheme.Ink));
        for (var i = 0; i < GameSettings.TextScaleNames.Length; i++)
        {
            textSize.AddItem(GameSettings.TextScaleNames[i], i);
        }

        textSize.AddThemeFontSizeOverride("font_size", 22);
        textSize.ItemSelected += index =>
        {
            settings.TextSize = (int)index;
            Commit(true);
        };
        sizeRow.AddChild(textSize);
        column.AddChild(sizeRow);
        lamp.Toggled += on =>
        {
            settings.ReadingLamp = on;
            Commit(false);
        };
        column.AddChild(lamp);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(UiTheme.Button("Done", () => Visible = false));
        column.AddChild(row);
    }

    /// <summary>Shows the settings.</summary>
    /// <param name="current">The settings to edit.</param>
    public void Open(GameSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        settings = current;
        fullscreen.SetPressedNoSignal(settings.Fullscreen);
        textSize.Select(settings.TextSize);
        lamp.SetPressedNoSignal(settings.ReadingLamp);
        Visible = true;
    }

    private void Commit(bool textSizeChanged)
    {
        settings.Save();
        settings.Apply();
        Changed(textSizeChanged);
    }
}
