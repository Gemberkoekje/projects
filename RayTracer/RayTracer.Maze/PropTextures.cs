using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace RayTracer.Gpu;

/// <summary>Atlas layer indices for the classic props (plan §3–§5).</summary>
internal enum DecalLayer
{
    OpenGlLogo = 0,
    SignExit = 1,
    SignArrow = 2,
    SignSmiley = 3,
    Rat = 4,
}

/// <summary>
/// Procedurally drawn <b>original</b> prop art (no Microsoft/SGI assets — plan §3.3 note):
/// a stylised "GL" logo, a few wall signs, and a rat sprite, packed into a fixed-size decal
/// atlas. Pixels are stored as <b>linear</b> RGBA (GDI draws in sRGB; we linearise) so the
/// shader can turn a texel straight into a per-wavelength reflectance via the
/// RGB→reflectance basis. Layout: <c>[(layer*Size*Size + y*Size + x)*4 + channel]</c>.
/// </summary>
internal sealed class PropTextures
{
    public int Size { get; }
    public int LayerCount { get; }
    public float[] Pixels { get; }

    private PropTextures(int size, int layerCount, float[] pixels)
    {
        Size = size;
        LayerCount = layerCount;
        Pixels = pixels;
    }

    public static PropTextures Build(int size = 128)
    {
        const int layers = 5;
        var pixels = new float[layers * size * size * 4];
        DrawLayer(pixels, size, (int)DecalLayer.OpenGlLogo, g => DrawOpenGlLogo(g, size));
        DrawLayer(pixels, size, (int)DecalLayer.SignExit, g => DrawTextSign(g, size, "EXIT", Color.FromArgb(20, 120, 40), Color.White));
        DrawLayer(pixels, size, (int)DecalLayer.SignArrow, g => DrawArrowSign(g, size));
        DrawLayer(pixels, size, (int)DecalLayer.SignSmiley, g => DrawSmiley(g, size));
        DrawLayer(pixels, size, (int)DecalLayer.Rat, g => DrawRat(g, size));
        return new PropTextures(size, layers, pixels);
    }

    // ── Drawing helpers ────────────────────────────────────────────────

    private static void DrawLayer(float[] dest, int size, int layer, Action<Graphics> draw)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            draw(g);
        }

        int baseIdx = layer * size * size * 4;
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[size * 4];
            for (int y = 0; y < size; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, size * 4);
                for (int x = 0; x < size; x++)
                {
                    // GDI 32bppArgb is BGRA in memory.
                    byte b = row[x * 4 + 0], gg = row[x * 4 + 1], r = row[x * 4 + 2], a = row[x * 4 + 3];
                    int o = baseIdx + (y * size + x) * 4;
                    dest[o + 0] = SrgbToLinear(r / 255f);
                    dest[o + 1] = SrgbToLinear(gg / 255f);
                    dest[o + 2] = SrgbToLinear(b / 255f);
                    dest[o + 3] = a / 255f;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static float SrgbToLinear(float s)
        => s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);

    private static void DrawOpenGlLogo(Graphics g, int size)
    {
        // Original stylised "GL" mark on a teal→blue plaque with a low swoosh accent.
        using var bg = new LinearGradientBrush(new Rectangle(0, 0, size, size),
            Color.FromArgb(16, 44, 66), Color.FromArgb(22, 96, 128), 55f);
        g.FillRectangle(bg, 0, 0, size, size);
        using var accent = new Pen(Color.FromArgb(120, 200, 230), size * 0.055f);
        g.DrawArc(accent, size * 0.10f, size * 0.50f, size * 0.80f, size * 0.60f, 200f, 140f);
        using var font = new Font("Arial", size * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.White);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("GL", font, text, new RectangleF(0, -size * 0.06f, size, size), fmt);
    }

    private static void DrawTextSign(Graphics g, int size, string label, Color bg, Color fg)
    {
        g.Clear(bg);
        using var border = new Pen(fg, size * 0.05f);
        g.DrawRectangle(border, size * 0.06f, size * 0.06f, size * 0.88f, size * 0.88f);
        using var font = new Font("Arial", size * 0.26f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(fg);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(label, font, text, new RectangleF(0, 0, size, size), fmt);
    }

    private static void DrawArrowSign(Graphics g, int size)
    {
        g.Clear(Color.FromArgb(210, 180, 30));
        using var arrow = new SolidBrush(Color.FromArgb(30, 30, 30));
        float m = size * 0.18f, cy = size * 0.5f, h = size * 0.18f;
        PointF[] pts =
        [
            new(m, cy - h), new(size * 0.58f, cy - h), new(size * 0.58f, cy - h * 1.9f),
            new(size - m, cy), new(size * 0.58f, cy + h * 1.9f), new(size * 0.58f, cy + h), new(m, cy + h),
        ];
        g.FillPolygon(arrow, pts);
    }

    private static void DrawSmiley(Graphics g, int size)
    {
        g.Clear(Color.FromArgb(40, 40, 48));
        using var face = new SolidBrush(Color.FromArgb(245, 210, 40));
        g.FillEllipse(face, size * 0.12f, size * 0.12f, size * 0.76f, size * 0.76f);
        using var dark = new SolidBrush(Color.FromArgb(30, 30, 30));
        g.FillEllipse(dark, size * 0.34f, size * 0.36f, size * 0.09f, size * 0.13f);
        g.FillEllipse(dark, size * 0.57f, size * 0.36f, size * 0.09f, size * 0.13f);
        using var smile = new Pen(Color.FromArgb(30, 30, 30), size * 0.06f);
        g.DrawArc(smile, size * 0.30f, size * 0.34f, size * 0.40f, size * 0.42f, 20, 140);
    }

    private static void DrawRat(Graphics g, int size)
    {
        // Simple side-on grey rat on a TRANSPARENT background — this layer is the animated
        // billboard sprite (§8), so only the rat shape is opaque (alpha-tested when composited).
        g.Clear(Color.Transparent);
        using var body = new SolidBrush(Color.FromArgb(150, 150, 158));
        g.FillEllipse(body, size * 0.20f, size * 0.42f, size * 0.50f, size * 0.30f);
        g.FillEllipse(body, size * 0.60f, size * 0.36f, size * 0.24f, size * 0.24f); // head
        using var ear = new SolidBrush(Color.FromArgb(180, 160, 170));
        g.FillEllipse(ear, size * 0.66f, size * 0.30f, size * 0.12f, size * 0.12f);
        using var eye = new SolidBrush(Color.Black);
        g.FillEllipse(eye, size * 0.76f, size * 0.44f, size * 0.045f, size * 0.045f);
        using var tail = new Pen(Color.FromArgb(180, 150, 150), size * 0.03f);
        g.DrawBezier(tail, new PointF(size * 0.22f, size * 0.56f), new PointF(size * 0.10f, size * 0.50f),
            new PointF(size * 0.08f, size * 0.70f), new PointF(size * 0.18f, size * 0.74f));
    }
}
