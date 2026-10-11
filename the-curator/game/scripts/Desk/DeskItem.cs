using System.Globalization;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Desk;

/// <summary>Something lying on the desk that opens when clicked: the newspaper, letters, the returns tray, the notebook, the catalogue drawer.</summary>
public partial class DeskItem : Button
{
    private readonly Label badge = new();
    private Color paper = new("e9dcc2");

    /// <summary>Styles the item.</summary>
    /// <param name="title">What it's called.</param>
    /// <param name="paperColor">Its colour.</param>
    /// <param name="inkColor">The colour of its words.</param>
    public void Setup(string title, Color paperColor, Color inkColor)
    {
        paper = paperColor;
        Text = title;
        FocusMode = FocusModeEnum.None;
        AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddThemeFontSizeOverride("font_size", 22);
        AddThemeColorOverride("font_color", inkColor);
        AddThemeColorOverride("font_hover_color", inkColor);
        AddThemeColorOverride("font_pressed_color", inkColor);
        AddThemeColorOverride("font_disabled_color", new Color(inkColor, 0.4f));
        Restyle(false);
        badge.AddThemeFontSizeOverride("font_size", 20);
        badge.AddThemeColorOverride("font_color", Colors.White);
        var badgeBox = UiTheme.Box(new Color("7a2e22"), new Color("4a1a12"), 1, 12, new Vector4(9, 1, 9, 1));
        badge.AddThemeStyleboxOverride("normal", badgeBox);
        badge.Position = new Vector2(8, 8);
        badge.Visible = false;
        AddChild(badge);
    }

    /// <summary>Shows a small count on the item, or hides it.</summary>
    /// <param name="count">The count; 0 hides it.</param>
    public void SetBadge(int count)
    {
        badge.Text = count.ToString(CultureInfo.InvariantCulture);
        badge.Visible = count > 0;
    }

    /// <summary>Marks the item as the one open on the desk.</summary>
    /// <param name="open">Whether it's open.</param>
    public void Restyle(bool open)
    {
        var border = open ? new Color("f6d48a") : paper.Darkened(0.45f);
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var back = state == "hover" ? paper.Lightened(0.08f) : state == "pressed" ? paper.Darkened(0.08f) : paper;
            var box = UiTheme.Box(back, border, open ? 3 : 1, 4, new Vector4(10, 10, 10, 10));
            box.ShadowColor = new Color(0f, 0f, 0f, 0.4f);
            box.ShadowSize = 5;
            box.ShadowOffset = new Vector2(3, 4);
            AddThemeStyleboxOverride(state, box);
        }
    }
}
