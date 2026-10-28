using System.Globalization;
using Vista.Sliders;

namespace Vista.App.Window;

/// <summary>
/// How a slider's value maps onto a track bar's whole positions (vista-plan §10 M1.16): evenly between <see cref="Min"/> and
/// <see cref="Max"/> in steps of <see cref="Step"/>, or, when <see cref="Logarithmic"/>, evenly in the value's logarithm over
/// <see cref="Positions"/> positions (a visibility from 1 to 500 km).
/// </summary>
internal sealed record TrackScale(double Min, double Max, double Step, bool Logarithmic = false, int Positions = 400)
{
    /// <summary>The track bar's last position (the first is 0).</summary>
    public int Last => Logarithmic ? Positions : (int)Math.Round((Max - Min) / Step);

    /// <summary>The position nearest <paramref name="value"/>, clamped to the track.</summary>
    public int PositionOf(double value)
    {
        double v = Math.Clamp(value, Min, Max);
        double t = Logarithmic ? Math.Log(v / Min) / Math.Log(Max / Min) * Positions : (v - Min) / Step;
        return Math.Clamp((int)Math.Round(t), 0, Last);
    }

    /// <summary>The value at <paramref name="position"/>.</summary>
    public double ValueAt(int position)
    {
        int p = Math.Clamp(position, 0, Last);
        double value = Logarithmic ? Min * Math.Pow(Max / Min, (double)p / Positions) : Min + p * Step;
        // A logarithmic value is kept to three significant figures, so the slider file reads like a number a person chose.
        return Logarithmic ? double.Parse(value.ToString("G3", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : Math.Clamp(value, Min, Max);
    }
}

/// <summary>
/// One linked slider in the window (vista-plan §3.4, §10 M1.16): its name, its editor, its value as resolved, whether it is auto or
/// pinned (and an "implausible" mark when a hint is about it), and a reset button that re-links it. The window sets the editor from the
/// resolved value with <see cref="Showing"/> set, so the editor's own change events can tell the user's moves from the window's.
/// </summary>
internal sealed class SliderRow
{
    private static readonly Color AutoColour = Color.FromArgb(0x2E, 0x7D, 0x32);
    private static readonly Color PinnedColour = Color.FromArgb(0x15, 0x65, 0xC0);
    private static readonly Color ImplausibleColour = Color.FromArgb(0xB2, 0x6A, 0x00);

    private readonly Action<SliderReading> _show;

    public SliderRow(Slider slider, string name, Control editor, Action<SliderReading> show, string tip)
    {
        Slider = slider;
        Editor = editor;
        _show = show;
        Name = new Label { Text = name, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Value = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        State = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Control.DefaultFont, FontStyle.Bold) };
        Reset = new Button { Text = "↺", Width = 28, Height = 24, Margin = new Padding(2), Enabled = false };
        editor.Dock = DockStyle.Fill;
        editor.Margin = new Padding(2, 3, 2, 3);
        Tip = tip;
    }

    /// <summary>The slider.</summary>
    public Slider Slider { get; }

    /// <summary>The slider's name.</summary>
    public Label Name { get; }

    /// <summary>The editor the user moves.</summary>
    public Control Editor { get; }

    /// <summary>The value as resolved, with its unit.</summary>
    public Label Value { get; }

    /// <summary>"auto" or "pinned".</summary>
    public Label State { get; }

    /// <summary>Re-links the slider (enabled only while it is pinned).</summary>
    public Button Reset { get; }

    /// <summary>What the slider is, for its tool tip.</summary>
    public string Tip { get; }

    /// <summary>True while the window sets the editor from the resolved value: its change events are not the user's.</summary>
    public bool Showing { get; private set; }

    /// <summary>Shows <paramref name="reading"/>: the value, auto or pinned and where an auto value came from, the implausible mark, the editor's position.</summary>
    public void Show(SliderReading reading, bool implausible, ToolTip tips)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Value.Text = reading.Value;
        State.Text = implausible ? "⚠ pinned" : reading.Pinned ? "pinned" : "auto";
        State.ForeColor = implausible ? ImplausibleColour : reading.Pinned ? PinnedColour : AutoColour;
        Reset.Enabled = reading.Pinned;
        tips.SetToolTip(State, reading.From);
        tips.SetToolTip(Value, reading.From);
        Showing = true;
        try
        {
            _show(reading);
        }
        finally
        {
            Showing = false;
        }
    }

    /// <summary>Adds the row's five cells to <paramref name="table"/> at <paramref name="row"/>.</summary>
    public void AddTo(TableLayoutPanel table, int row, ToolTip tips)
    {
        table.Controls.Add(Name, 0, row);
        table.Controls.Add(Editor, 1, row);
        table.Controls.Add(Value, 2, row);
        table.Controls.Add(State, 3, row);
        table.Controls.Add(Reset, 4, row);
        tips.SetToolTip(Name, Tip);
        tips.SetToolTip(Reset, "Reset: auto again, following the sliders above it");
    }
}
