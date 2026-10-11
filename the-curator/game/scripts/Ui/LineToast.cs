using Curator.Presentation.Game;
using Godot;

namespace Curator.Presentation.Ui;

/// <summary>
/// A line at the foot of the screen that fades: refusals as gentle in-world lines (never errors),
/// what Identify turned up, and what the patron says while the curator is looking down.
/// </summary>
public partial class LineToast : Control, IGameView
{
    private const float Seconds = 4f;
    private readonly PanelContainer panel = new();
    private readonly Label line = UiTheme.Text("", UiTheme.PrintItalic, 22, UiTheme.SpeechInk);
    // A fade may or may not be running; null means none (a justified nullable: entity or none).
    private Tween? fade;

    /// <summary>The last line shown, for screenshots and tests.</summary>
    public string Current => line.Text;

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(0, 930);
        Size = new Vector2(1920, 90);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.SpeechBack, new Color(0.95f, 0.85f, 0.65f, 0.35f), 1, 8, new Vector4(22, 10, 22, 10)));
        panel.MouseFilter = MouseFilterEnum.Ignore;
        line.HorizontalAlignment = HorizontalAlignment.Center;
        line.CustomMinimumSize = new Vector2(0, 0);
        panel.AddChild(line);
        center.AddChild(panel);
        panel.Modulate = new Color(1f, 1f, 1f, 0f);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Looking down, the foot of the screen is all documents; the counter band at the top is free.
        Position = new Vector2(0, snapshot.LookingUp ? 930 : 120);
    }

    /// <summary>Shows a line in the narrator's voice.</summary>
    /// <param name="text">The line.</param>
    public void Say(string text) => ShowLine(text);

    /// <summary>Shows what a patron said.</summary>
    /// <param name="name">Who said it.</param>
    /// <param name="text">What they said.</param>
    public void Speak(string name, string text) => ShowLine($"{name}: “{text}”");

    private void ShowLine(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        line.Text = text;
        line.AutowrapMode = text.Length > 90 ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
        line.CustomMinimumSize = new Vector2(text.Length > 90 ? 1100 : 0, 0);
        fade?.Kill();
        panel.Modulate = Colors.White;
        fade = CreateTween();
        fade.TweenInterval(Seconds);
        fade.TweenProperty(panel, "modulate:a", 0f, 0.8f);
    }
}
