using Godot;

namespace Curator.Presentation.Ui;

/// <summary>A row of round pips: filled for what's left, hollow for what's spent.</summary>
public partial class Pips : Control
{
    private const float Radius = 8f;
    private const float Gap = 6f;
    private int filled;
    private int total;

    /// <summary>The colour of the pips.</summary>
    public Color Color { get; set; } = UiTheme.SpeechInk;

    /// <summary>Shows a count.</summary>
    /// <param name="filledPips">Pips left.</param>
    /// <param name="totalPips">All pips, at least <paramref name="filledPips"/>.</param>
    public void Show(int filledPips, int totalPips)
    {
        filled = Math.Max(0, filledPips);
        total = Math.Max(filled, totalPips);
        CustomMinimumSize = new Vector2(total * ((Radius * 2) + Gap), (Radius * 2) + 4);
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        for (var i = 0; i < total; i++)
        {
            var center = new Vector2(Radius + (i * ((Radius * 2) + Gap)), Size.Y / 2f);
            if (i < filled)
            {
                DrawCircle(center, Radius, Color);
            }
            else
            {
                DrawArc(center, Radius - 1f, 0f, Mathf.Tau, 24, new Color(Color, 0.6f), 1.5f, true);
            }
        }
    }
}
