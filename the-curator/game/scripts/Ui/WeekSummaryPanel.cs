using System.Globalization;
using Curator.Core.Projections;
using Godot;

namespace Curator.Presentation.Ui;

/// <summary>
/// The week's end (BUILD_BRIEF §5.15): money, loans and declines, outcomes heard and never heard, each
/// story patron's trust, and the tutorial book's state.
/// </summary>
public partial class WeekSummaryPanel : Control
{
    private readonly VBoxContainer body = new();
    private Action closed = () => { };

    /// <inheritdoc />
    public override void _Ready()
    {
        Visible = false;
        Size = new Vector2(1920, 1080);
        var dim = new ColorRect { Color = new Color(0.02f, 0.02f, 0.05f, 0.7f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var sheet = new PanelContainer();
        sheet.AddThemeStyleboxOverride("panel", UiTheme.Sheet(UiTheme.Paper, 34));
        center.AddChild(sheet);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        sheet.AddChild(column);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(1000, 760), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(body);
        column.AddChild(scroll);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(UiTheme.Button("Back to the title", () =>
        {
            Visible = false;
            closed();
        }));
        column.AddChild(row);
    }

    /// <summary>Shows the week's summary.</summary>
    /// <param name="week">The summary.</param>
    /// <param name="onClosed">What happens when it's put away.</param>
    public void Open(WeekSummaryView week, Action onClosed)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(onClosed);
        closed = onClosed;
        foreach (var child in body.GetChildren())
        {
            child.QueueFree();
        }

        var culture = CultureInfo.InvariantCulture;
        body.AddChild(UiTheme.Text("The week's end", UiTheme.Print, 40, UiTheme.Ink));
        body.AddChild(UiTheme.Text(string.Create(culture, $"{UiText.Money} {week.Money}   ·   {week.Loans} lent   ·   {week.Declines} turned away   ·   {week.WalkOuts} walked out"), UiTheme.Print, 24, UiTheme.Ink));
        Heading("Heard");
        if (week.Heard.Count == 0)
        {
            Line(UiText.Nothing, UiTheme.Faint);
        }

        foreach (var heard in week.Heard)
        {
            Line(string.Create(culture, $"Day {heard.Day}, {UiText.Channel(heard.Channel).ToLowerInvariant()}: {(heard.Headline.Length > 0 ? $"{heard.Headline}. " : "")}{heard.Text}"), UiTheme.Ink);
        }

        if (week.LateWord.Count > 0)
        {
            Heading("Late word");
            foreach (var word in week.LateWord)
            {
                Line(word, UiTheme.Ink);
            }
        }

        Line(string.Create(culture, $"Never heard of: {week.NeverHeard} {(week.NeverHeard == 1 ? "loan" : "loans")}."), UiTheme.Faint);
        Heading("Where things stand");
        foreach (var patron in week.StoryPatrons)
        {
            var trust = patron.Trust >= 0 && patron.Trust < UiText.TrustWords.Length ? UiText.TrustWords[patron.Trust] : "";
            Line(patron.Met ? $"{patron.Name}: {trust}" : $"{patron.Name}: never came", UiTheme.Ink);
        }

        var book = week.TutorialBookBack
            ? week.TutorialPagesRemoved > 0
                ? string.Create(culture, $"{week.TutorialBookTitle} came back with {week.TutorialPagesRemoved} {(week.TutorialPagesRemoved == 1 ? "page" : "pages")} missing.")
                : $"{week.TutorialBookTitle} came back whole."
            : $"{week.TutorialBookTitle} hasn't come back.";
        Line(book, UiTheme.Ink);
        Visible = true;
    }

    private void Heading(string text) => body.AddChild(UiTheme.Text(text, UiTheme.PrintItalic, 24, UiTheme.Faint));

    private void Line(string text, Color color)
    {
        body.AddChild(UiTheme.Indent(UiTheme.Para(text, UiTheme.Print, 21, color), 24));
    }
}
