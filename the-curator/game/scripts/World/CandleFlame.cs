using Curator.Presentation.Art;
using Godot;

namespace Curator.Presentation.World;

/// <summary>
/// The candle flame: four frames on slightly irregular timing (BUILD_BRIEF §7.4). Uses the
/// candle_flame_0–3 paintings when they exist, else draws a flat teardrop that changes shape.
/// </summary>
public partial class CandleFlame : Node2D
{
    private static readonly float[] FrameSeconds = [0.11f, 0.14f, 0.09f, 0.13f];
    private readonly List<Texture2D> frames = [];
    private int frame;
    private float untilNext;

    /// <summary>Loads the flame frames named in the layout.</summary>
    /// <param name="frameIds">The frame slot ids.</param>
    public void Setup(IReadOnlyList<string> frameIds)
    {
        ArgumentNullException.ThrowIfNull(frameIds);
        foreach (var id in frameIds)
        {
            if (ArtLibrary.TryGet(id, out var texture))
            {
                frames.Add(texture);
            }
        }

        untilNext = FrameSeconds[0];
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        untilNext -= (float)delta;
        if (untilNext > 0)
        {
            return;
        }

        frame = (frame + 1) % FrameSeconds.Length;
        untilNext = FrameSeconds[frame];
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (frames.Count == FrameSeconds.Length)
        {
            var texture = frames[frame];
            DrawTexture(texture, -texture.GetSize() / 2f);
            return;
        }

        var lean = (frame - 1.5f) * 1.6f;
        var height = 30f + (frame % 2 * 4f);
        var outer = Teardrop(10f, height, lean);
        var inner = Teardrop(5f, height * 0.6f, lean * 0.6f);
        DrawColoredPolygon(outer, new Color(1f, 0.62f, 0.2f, 0.92f));
        DrawColoredPolygon(inner, new Color(1f, 0.93f, 0.7f, 0.95f));
    }

    private static Vector2[] Teardrop(float radius, float height, float lean)
    {
        var points = new List<Vector2>();
        for (var i = 0; i <= 16; i++)
        {
            var angle = Mathf.Pi * i / 16f;
            points.Add(new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.8f));
        }

        points.Add(new Vector2(lean, -height));
        return [.. points];
    }
}
