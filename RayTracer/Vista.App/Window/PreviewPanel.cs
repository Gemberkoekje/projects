using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vista.Cli;

namespace Vista.App.Window;

/// <summary>
/// The window's preview (vista-plan §10 M1.16): the image fitted to the panel, drawn smoothly (a 320 px preview is enlarged three times
/// in a full-screen window; a pixel-for-pixel enlargement read as a mosaic), with a caption in its corner.
/// </summary>
internal sealed class PreviewPanel : Control
{
    private Bitmap? _image;

    public PreviewPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(22, 22, 22);
        ForeColor = Color.Gainsboro;
    }

    /// <summary>The image shown; null for none.</summary>
    public Bitmap? Image => _image;

    /// <summary>The caption in the bottom-left corner.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption;
        set
        {
            _caption = value;
            Invalidate();
        }
    }

    private string _caption = string.Empty;

    /// <summary>Shows <paramref name="image"/>, which the panel then owns.</summary>
    public void Show(Bitmap image)
    {
        Bitmap? old = _image;
        _image = image;
        old?.Dispose();
        Invalidate();
    }

    /// <summary>An 8-bit sRGB image as a bitmap.</summary>
    public static Bitmap ToBitmap(PreviewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format24bppRgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    int s = (y * frame.Width + x) * 3;
                    row[x * 3] = frame.Rgb[s + 2];
                    row[x * 3 + 1] = frame.Rgb[s + 1];
                    row[x * 3 + 2] = frame.Rgb[s];
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        if (_image is { } image)
        {
            double scale = Math.Min((double)ClientSize.Width / image.Width, (double)ClientSize.Height / image.Height);
            int w = Math.Max(1, (int)(image.Width * scale)), h = Math.Max(1, (int)(image.Height * scale));
            var target = new Rectangle((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, target);
        }
        if (_caption.Length > 0)
        {
            using var brush = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            SizeF size = g.MeasureString(_caption, Font);
            g.FillRectangle(brush, 6, ClientSize.Height - size.Height - 10, size.Width + 8, size.Height + 4);
            TextRenderer.DrawText(g, _caption, Font, new Point(10, (int)(ClientSize.Height - size.Height - 8)), ForeColor);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _image?.Dispose();
        base.Dispose(disposing);
    }
}
