using Godot;

namespace Curator.Presentation.Ui;

/// <summary>A yes-or-no question over a dimmed screen: closing while patrons are still to come.</summary>
public partial class ConfirmPanel : Control
{
    private readonly Label question = UiTheme.Para("", UiTheme.Print, 26, UiTheme.Ink);
    private Action yes = () => { };

    /// <inheritdoc />
    public override void _Ready()
    {
        Visible = false;
        Size = new Vector2(1920, 1080);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var sheet = new PanelContainer();
        sheet.AddThemeStyleboxOverride("panel", UiTheme.Sheet(UiTheme.Paper, 28));
        center.AddChild(sheet);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 22);
        sheet.AddChild(column);
        question.CustomMinimumSize = new Vector2(560, 0);
        question.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(question);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 18);
        buttons.AddChild(UiTheme.Button(UiText.No, () => Visible = false));
        buttons.AddChild(UiTheme.Button(UiText.Yes, () =>
        {
            Visible = false;
            yes();
        }));
        column.AddChild(buttons);
    }

    /// <summary>Asks the question.</summary>
    /// <param name="text">The question.</param>
    /// <param name="onYes">What happens on yes.</param>
    public void Ask(string text, Action onYes)
    {
        ArgumentNullException.ThrowIfNull(onYes);
        question.Text = text;
        yes = onYes;
        Visible = true;
    }
}
