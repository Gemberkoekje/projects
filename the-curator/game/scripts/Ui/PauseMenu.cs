using Godot;

namespace Curator.Presentation.Ui;

/// <summary>Esc pauses (BUILD_BRIEF §7.12): resume, settings, back to the title, quit.</summary>
public partial class PauseMenu : Control
{
    private readonly VBoxContainer column = new() { CustomMinimumSize = new Vector2(480, 0) };

    /// <summary>Raised for Settings.</summary>
    public event Action Settings = delegate { };

    /// <summary>Raised for Back to the title.</summary>
    public event Action LeaveForTitle = delegate { };

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
        column.AddThemeConstantOverride("separation", 14);
        sheet.AddChild(column);
        Rebuild();
    }

    /// <summary>Builds the menu's contents again, at the current text size.</summary>
    public void Rebuild()
    {
        foreach (var child in column.GetChildren())
        {
            column.RemoveChild(child);
            child.QueueFree();
        }

        column.AddChild(UiTheme.Text("Paused", UiTheme.Print, 36, UiTheme.Ink));
        column.AddChild(UiTheme.Button("Resume", () => Visible = false));
        column.AddChild(UiTheme.Button("Settings", () => Settings()));
        column.AddChild(UiTheme.Button("Back to the title", () =>
        {
            Visible = false;
            LeaveForTitle();
        }));
        column.AddChild(UiTheme.Para("The game saves each evening. Leaving now loses today, and Continue starts this morning again.", UiTheme.PrintItalic, 19, UiTheme.Faint));
        column.AddChild(UiTheme.Button("Quit", () => GetTree().Quit()));
    }
}
