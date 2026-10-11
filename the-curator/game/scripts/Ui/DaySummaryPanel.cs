using System.Globalization;
using Curator.Core.Projections;
using Godot;

namespace Curator.Presentation.Ui;

/// <summary>The evening's summary (GDD §4): what went out and came back, what was heard, fees, upkeep, tomorrow's mana.</summary>
public partial class DaySummaryPanel : Control
{
    private readonly VBoxContainer body = new();
    private readonly Button next = UiTheme.Button(UiText.NextMorning, () => { });
    private Action closed = () => { };

    /// <inheritdoc />
    public override void _Ready()
    {
        Visible = false;
        Size = new Vector2(1920, 1080);
        var dim = new ColorRect { Color = new Color(0.02f, 0.02f, 0.05f, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var sheet = new PanelContainer();
        sheet.AddThemeStyleboxOverride("panel", UiTheme.Sheet(UiTheme.Paper, 34));
        center.AddChild(sheet);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        sheet.AddChild(column);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(900, 700),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(body);
        column.AddChild(scroll);
        next.Pressed += () =>
        {
            Visible = false;
            closed();
        };
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(next);
        column.AddChild(row);
    }

    /// <summary>Shows a day's summary.</summary>
    /// <param name="day">The summary.</param>
    /// <param name="weekOver">Whether this was the week's last day.</param>
    /// <param name="onClosed">What happens when it's put away.</param>
    public void Open(DaySummaryView day, bool weekOver, Action onClosed)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(onClosed);
        closed = onClosed;
        foreach (var child in body.GetChildren())
        {
            child.QueueFree();
        }

        var culture = CultureInfo.InvariantCulture;
        body.AddChild(UiTheme.Text($"Day {day.Day} — {day.Date}", UiTheme.Print, 36, UiTheme.Ink));
        Section(UiText.Lent, day.Lent.Select(l => string.Create(culture, $"{l.BookTitle} to {l.PatronName}, {l.Fee}{(l.AsAlternative ? $" ({UiText.Alternative})" : "")}")));
        Section(UiText.Returned, day.Returned);
        Section(UiText.TurnedAway, day.Declined);
        Section(UiText.WalkedOut, day.WalkedOut);
        Section(UiText.Heard, day.Heard.Select(h => $"{UiText.Channel(h.Channel)}: {(h.Headline.Length > 0 ? $"{h.Headline}. " : "")}{h.Text}"));
        var money = string.Create(culture, $"{UiText.Fees} +{day.Fees}   ·   {UiText.Upkeep} –{day.Upkeep}   ·   {UiText.Money} {day.Money}");
        body.AddChild(new HSeparator());
        body.AddChild(UiTheme.Text(money, UiTheme.Print, 24, UiTheme.Ink));
        if (day.HasTomorrow)
        {
            body.AddChild(UiTheme.Text(string.Create(culture, $"{UiText.TomorrowMana}: {day.TomorrowMana}"), UiTheme.Print, 24, UiTheme.Ink));
        }

        next.Text = weekOver ? UiText.WeekEnds : UiText.NextMorning;
        Visible = true;
    }

    private void Section(string heading, IEnumerable<string> lines)
    {
        var items = lines.ToList();
        body.AddChild(UiTheme.Text(heading, UiTheme.PrintItalic, 24, UiTheme.Faint));
        if (items.Count == 0)
        {
            body.AddChild(UiTheme.Indent(UiTheme.Text(UiText.Nothing, UiTheme.Print, 21, UiTheme.Faint), 24));
            return;
        }

        foreach (var item in items)
        {
            body.AddChild(UiTheme.Indent(UiTheme.Para(item, UiTheme.Print, 21, UiTheme.Ink), 24));
        }
    }
}
