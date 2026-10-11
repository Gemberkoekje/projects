using Godot;

namespace Curator.Presentation.Art;

/// <summary>
/// Flat procedural stand-ins for paintings that don't exist yet: shapes and colours only, no
/// generated image files (BUILD_BRIEF §7.3). Drawing is deterministic for a slot.
/// </summary>
public static class PlaceholderPainter
{
    private static readonly Color[] SpineColors =
    [
        new(0.36f, 0.16f, 0.12f), new(0.20f, 0.26f, 0.18f), new(0.17f, 0.20f, 0.29f), new(0.42f, 0.30f, 0.16f),
        new(0.29f, 0.17f, 0.26f), new(0.46f, 0.40f, 0.30f), new(0.24f, 0.13f, 0.10f), new(0.14f, 0.24f, 0.26f),
    ];

    /// <summary>Draws a slot's placeholder into its own local rect (origin at the slot's corner).</summary>
    /// <param name="canvas">The node drawing.</param>
    /// <param name="slot">The slot.</param>
    public static void Draw(CanvasItem canvas, ArtSlot slot)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(slot);
        var size = slot.Rect.Size;
        var rng = new RandomNumberGenerator { Seed = StableHash(slot.Id) };
        switch (slot.Placeholder)
        {
            case "stone":
                Stone(canvas, size, slot.Color);
                break;
            case "wallArch":
                WallArch(canvas, slot, slot.Color);
                break;
            case "silhouette":
                Silhouette(canvas, size, slot.Color);
                break;
            case "shelf":
                Shelf(canvas, size, slot.Color, rng);
                break;
            case "cabinet":
                Cabinet(canvas, size, slot.Color);
                break;
            case "woodDark":
            case "woodLight":
                Wood(canvas, size, slot.Color, rng, slot.Placeholder == "woodDark");
                break;
            case "candle":
                Candle(canvas, size, slot.Color);
                break;
            default:
                canvas.DrawRect(new Rect2(Vector2.Zero, size), slot.Color);
                break;
        }
    }

    // string.GetHashCode is randomised per process; placeholders must look the same every run.
    private static ulong StableHash(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in text)
        {
            hash = unchecked((hash ^ c) * 1099511628211UL);
        }

        return hash;
    }

    private static void Stone(CanvasItem canvas, Vector2 size, Color color)
    {
        canvas.DrawRect(new Rect2(Vector2.Zero, size), color);
        var line = color.Darkened(0.25f);
        for (var row = 0; row * 56f < size.Y; row++)
        {
            var y = row * 56f;
            canvas.DrawLine(new Vector2(0, y), new Vector2(size.X, y), line, 2f);
            for (var x = row % 2 == 0 ? 0f : 60f; x < size.X; x += 120f)
            {
                canvas.DrawLine(new Vector2(x, y), new Vector2(x, y + 56f), line, 2f);
            }
        }

        canvas.DrawRect(new Rect2(0, size.Y * 0.82f, size.X, size.Y * 0.18f), color.Darkened(0.12f));
    }

    private static void WallArch(CanvasItem canvas, ArtSlot slot, Color color)
    {
        var size = slot.Rect.Size;
        var arch = new Rect2(slot.Arch.Position - slot.Rect.Position, slot.Arch.Size);
        var radius = arch.Size.X / 2f;
        var spring = arch.Position.Y + radius;
        canvas.DrawRect(new Rect2(0, 0, arch.Position.X, size.Y), color);
        canvas.DrawRect(new Rect2(arch.End.X, 0, size.X - arch.End.X, size.Y), color);
        canvas.DrawRect(new Rect2(arch.Position.X, 0, arch.Size.X, arch.Position.Y), color);
        var spandrel = new List<Vector2> { new(arch.Position.X, arch.Position.Y), new(arch.End.X, arch.Position.Y), new(arch.End.X, spring) };
        var center = new Vector2(arch.Position.X + radius, spring);
        for (var i = 0; i <= 32; i++)
        {
            var angle = Mathf.Pi * i / 32f;
            spandrel.Add(center + new Vector2(Mathf.Cos(angle) * radius, -Mathf.Sin(angle) * radius));
        }

        canvas.DrawColoredPolygon([.. spandrel], color);
        var trim = color.Lightened(0.18f);
        canvas.DrawArc(center, radius + 14f, Mathf.Pi, Mathf.Tau, 48, trim, 28f);
        canvas.DrawRect(new Rect2(arch.Position.X - 28f, spring, 28f, size.Y - spring), trim);
        canvas.DrawRect(new Rect2(arch.End.X, spring, 28f, size.Y - spring), trim);
    }

    private static void Silhouette(CanvasItem canvas, Vector2 size, Color color)
    {
        var w = size.X;
        var shoulders = new Vector2[]
        {
            new(w * 0.10f, size.Y), new(w * 0.16f, size.Y * 0.52f), new(w * 0.30f, size.Y * 0.40f),
            new(w * 0.70f, size.Y * 0.40f), new(w * 0.84f, size.Y * 0.52f), new(w * 0.90f, size.Y),
        };
        canvas.DrawColoredPolygon(shoulders, color);
        var hood = new List<Vector2>();
        var center = new Vector2(w * 0.5f, size.Y * 0.27f);
        for (var i = 0; i < 40; i++)
        {
            var angle = Mathf.Tau * i / 40f;
            var stretch = Mathf.Sin(angle) < 0 ? 1.25f : 1f;
            hood.Add(center + new Vector2(Mathf.Cos(angle) * w * 0.23f, Mathf.Sin(angle) * w * 0.27f * stretch));
        }

        canvas.DrawColoredPolygon([.. hood], color);
        var face = new List<Vector2>();
        var faceCenter = new Vector2(w * 0.5f, size.Y * 0.31f);
        for (var i = 0; i < 24; i++)
        {
            var angle = Mathf.Tau * i / 24f;
            face.Add(faceCenter + new Vector2(Mathf.Cos(angle) * w * 0.13f, Mathf.Sin(angle) * w * 0.17f));
        }

        canvas.DrawColoredPolygon([.. face], color.Darkened(0.45f));
    }

    private static void Shelf(CanvasItem canvas, Vector2 size, Color color, RandomNumberGenerator rng)
    {
        canvas.DrawRect(new Rect2(Vector2.Zero, size), color.Darkened(0.35f));
        const float ShelfHeight = 142f;
        for (var top = 40f; top + ShelfHeight <= size.Y + 20f; top += ShelfHeight)
        {
            var x = 26f;
            while (x < size.X - 40f)
            {
                var width = rng.RandfRange(14f, 34f);
                var height = rng.RandfRange(88f, 124f);
                var spine = SpineColors[rng.RandiRange(0, SpineColors.Length - 1)].Lightened(rng.RandfRange(0f, 0.12f));
                if (rng.Randf() < 0.07f)
                {
                    x += width;
                    continue;
                }

                canvas.DrawRect(new Rect2(x, top + ShelfHeight - 16f - height, width, height), spine);
                canvas.DrawLine(new Vector2(x + 3f, top + ShelfHeight - height + 8f), new Vector2(x + width - 3f, top + ShelfHeight - height + 8f), spine.Lightened(0.25f), 2f);
                x += width + 1f;
            }

            canvas.DrawRect(new Rect2(0, top + ShelfHeight - 16f, size.X, 16f), color);
        }

        canvas.DrawRect(new Rect2(0, 0, 22f, size.Y), color);
        canvas.DrawRect(new Rect2(size.X - 22f, 0, 22f, size.Y), color);
    }

    private static void Cabinet(CanvasItem canvas, Vector2 size, Color color)
    {
        canvas.DrawRect(new Rect2(Vector2.Zero, size), color);
        canvas.DrawRect(new Rect2(18f, 18f, size.X - 36f, size.Y - 36f), color.Darkened(0.45f));
        for (var x = 40f; x < size.X - 24f; x += 34f)
        {
            canvas.DrawLine(new Vector2(x, 18f), new Vector2(x, size.Y - 18f), color.Lightened(0.25f), 5f);
        }

        canvas.DrawLine(new Vector2(18f, size.Y * 0.35f), new Vector2(size.X - 18f, size.Y * 0.35f), color.Lightened(0.2f), 6f);
        canvas.DrawLine(new Vector2(18f, size.Y * 0.7f), new Vector2(size.X - 18f, size.Y * 0.7f), color.Lightened(0.2f), 6f);
        canvas.DrawRect(new Rect2(size.X / 2f - 18f, size.Y * 0.47f, 36f, 46f), new Color(0.48f, 0.40f, 0.22f));
    }

    private static void Wood(CanvasItem canvas, Vector2 size, Color color, RandomNumberGenerator rng, bool dark)
    {
        canvas.DrawRect(new Rect2(Vector2.Zero, size), color);
        var plank = dark ? 90f : 120f;
        for (var y = plank; y < size.Y; y += plank)
        {
            canvas.DrawLine(new Vector2(0, y), new Vector2(size.X, y), color.Darkened(0.3f), 3f);
        }

        for (var i = 0; i < size.Y / 6f; i++)
        {
            var y = rng.RandfRange(0f, size.Y);
            var x = rng.RandfRange(0f, size.X);
            canvas.DrawLine(new Vector2(x, y), new Vector2(x + rng.RandfRange(80f, 260f), y + rng.RandfRange(-2f, 2f)), color.Darkened(rng.RandfRange(0.05f, 0.15f)), 1.5f);
        }

        if (dark)
        {
            canvas.DrawRect(new Rect2(0, 0, size.X, 18f), color.Lightened(0.15f));
            canvas.DrawRect(new Rect2(0, 18f, size.X, 6f), color.Darkened(0.35f));
        }
    }

    private static void Candle(CanvasItem canvas, Vector2 size, Color color)
    {
        var body = new Rect2(size.X / 2f - 18f, 26f, 36f, size.Y - 76f);
        canvas.DrawRect(body, color);
        canvas.DrawRect(new Rect2(body.Position.X, body.Position.Y, 8f, body.Size.Y), color.Darkened(0.12f));
        canvas.DrawLine(new Vector2(size.X / 2f, 26f), new Vector2(size.X / 2f, 14f), new Color(0.15f, 0.12f, 0.1f), 2f);
        var brass = new Color(0.62f, 0.48f, 0.24f);
        canvas.DrawRect(new Rect2(10f, size.Y - 52f, size.X - 20f, 16f), brass);
        canvas.DrawRect(new Rect2(26f, size.Y - 38f, size.X - 52f, 30f), brass.Darkened(0.2f));
    }
}
