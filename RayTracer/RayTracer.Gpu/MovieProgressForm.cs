using System.Drawing;
using System.Windows.Forms;

namespace RayTracer.Gpu;

/// <summary>
/// The non-modal progress window shown while <c>Program.RunMovie</c> renders the walk headless. It shows
/// the frame count, a progress bar, a live thumbnail of the most-recently rendered frame (so the user can
/// see what's baking — a long Beautiful render is otherwise just a crawling bar), and a Cancel button that
/// stops capture early (the frames rendered so far are still encoded). The render runs on the UI thread,
/// so the caller pumps <see cref="Application.DoEvents"/> between frames to keep this responsive.
/// </summary>
internal sealed class MovieProgressForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _label;
    private readonly Button _cancel;
    private readonly PictureBox _preview;
    private readonly Label _previewHint;

    /// <summary>Set once the user clicks Cancel; the capture loop polls it to stop early.</summary>
    public bool Cancelled { get; private set; }

    /// <param name="max">Bar range — total frames (Duration length) or total walks (Walks length).</param>
    public MovieProgressForm(int max)
    {
        Text = "Recording movie…";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false; // no close box — Cancel is the way out
        ClientSize = new Size(440, 320);

        _label = new Label { Left = 16, Top = 12, Width = 408, Height = 22, Text = "Starting…" };
        _bar = new ProgressBar { Left = 16, Top = 38, Width = 408, Height = 20, Minimum = 0, Maximum = Math.Max(1, max), Value = 0 };

        // Live thumbnail of the frame currently being rendered.
        _preview = new PictureBox { Left = 42, Top = 68, Width = 356, Height = 200, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        _previewHint = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gainsboro, Text = "Rendering…" };
        _preview.Controls.Add(_previewHint);

        _cancel = new Button { Text = "Cancel", Left = 348, Top = 282, Width = 76, Height = 26 };
        _cancel.Click += (_, _) =>
        {
            Cancelled = true;
            _cancel.Enabled = false;
            _label.Text = "Cancelling — finishing the current frame…";
        };

        Controls.AddRange([_label, _bar, _preview, _cancel]);
        FormClosed += (_, _) => _preview.Image?.Dispose();
    }

    /// <summary>Advance the bar to <paramref name="value"/> and set the status line.</summary>
    public void Report(int value, string label)
    {
        _bar.Value = Math.Clamp(value, _bar.Minimum, _bar.Maximum);
        if (!Cancelled) _label.Text = label; // keep the "cancelling…" note once cancel is pressed
    }

    /// <summary>Shows the latest rendered frame. Takes ownership of <paramref name="img"/> and disposes the
    /// previously shown one.</summary>
    public void SetPreview(Bitmap img)
    {
        _previewHint.Visible = false;
        Image? old = _preview.Image;
        _preview.Image = img;
        old?.Dispose();
    }

    /// <summary>Swap the caption to a free-form status (e.g. the encode phase).</summary>
    public void SetStatus(string text)
    {
        _label.Text = text;
        _cancel.Enabled = false; // past the point where cancelling capture helps
    }
}
