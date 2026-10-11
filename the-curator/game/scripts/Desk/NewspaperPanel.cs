using Curator.Presentation.Game;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Desk;

/// <summary>The morning paper (BUILD_BRIEF §5.1, §7.5): outcomes among the ordinary news, and a line about the library.</summary>
public partial class NewspaperPanel : Control, IGameView
{
    private readonly VBoxContainer body = new();
    private string shownKey = "";
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the newspaper first.");

    /// <summary>Connects the view to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    public void Bind(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override void _Ready()
    {
        Size = new Vector2(1166, 696);
        var sheet = new PanelContainer { Size = new Vector2(980, 696), Position = new Vector2(93, 0) };
        sheet.CustomMinimumSize = sheet.Size;
        sheet.AddThemeStyleboxOverride("panel", UiTheme.Sheet(UiTheme.Newsprint, 30));
        AddChild(sheet);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(body);
        sheet.AddChild(scroll);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var paper = snapshot.Morning.Newspaper;
        var key = $"{paper.Date}|{paper.Delivered}|{paper.Headlines.Count}|{UiTheme.TextScale}";
        if (key == shownKey)
        {
            return;
        }

        shownKey = key;
        foreach (var child in body.GetChildren())
        {
            child.QueueFree();
        }

        var masthead = UiTheme.Para(paper.Masthead, UiTheme.Print, 46, UiTheme.Ink);
        masthead.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(masthead);
        var date = UiTheme.Para(paper.Date, UiTheme.PrintItalic, 20, UiTheme.Faint);
        date.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(date);
        body.AddChild(Rule(2));
        if (!paper.Delivered)
        {
            body.AddChild(UiTheme.Text(UiText.NoPaper, UiTheme.PrintItalic, 24, UiTheme.Faint));
        }
        else
        {
            foreach (var headline in paper.Headlines)
            {
                body.AddChild(UiTheme.Para(headline.Headline, UiTheme.Print, 30, UiTheme.Ink));
                body.AddChild(UiTheme.Para(headline.Text, UiTheme.Print, 21, UiTheme.Ink));
                body.AddChild(Rule(1));
            }

            foreach (var item in paper.Flavour)
            {
                body.AddChild(UiTheme.Para($"•  {item}", UiTheme.Print, 22, UiTheme.Ink));
            }

            if (paper.ToneLine.Length > 0)
            {
                body.AddChild(Rule(1));
                body.AddChild(UiTheme.Para(paper.ToneLine, UiTheme.PrintItalic, 21, UiTheme.Ink));
            }
        }

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(UiTheme.Button(UiText.Close, () => Game.Open(DeskDocument.None)));
        body.AddChild(row);
    }

    private static HSeparator Rule(int thickness)
    {
        var rule = new HSeparator();
        rule.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(0.17f, 0.13f, 0.09f, 0.6f), Thickness = thickness });
        return rule;
    }
}
