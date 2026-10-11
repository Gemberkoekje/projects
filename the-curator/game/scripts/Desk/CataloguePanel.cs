using System.Globalization;
using Curator.Core.Game;
using Curator.Core.Projections;
using Curator.Presentation.Game;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Desk;

/// <summary>
/// The card-catalogue drawer (BUILD_BRIEF §7.5): an index card per book with its status, fee, pages
/// read and spine tag. Clicking a card puts the book on the desk.
/// </summary>
public partial class CataloguePanel : Control, IGameView
{
    private readonly GridContainer cards = new() { Columns = 3 };
    private string shownKey = "";
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the catalogue first.");

    /// <summary>Connects the view to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    public void Bind(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override void _Ready()
    {
        Size = new Vector2(1166, 696);
        var drawer = new PanelContainer { Size = Size, CustomMinimumSize = Size };
        drawer.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color("5a3d26"), new Color("2e1d10"), 3, 6, new Vector4(20, 16, 20, 16)));
        AddChild(drawer);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        var header = new HBoxContainer();
        var label = UiTheme.Text(UiText.Catalogue, UiTheme.Print, 28, UiTheme.SpeechInk);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(label);
        header.AddChild(UiTheme.Button(UiText.Close, () => Game.Open(DeskDocument.None)));
        column.AddChild(header);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("h_separation", 14);
        cards.AddThemeConstantOverride("v_separation", 14);
        scroll.AddChild(cards);
        column.AddChild(scroll);
        drawer.AddChild(column);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var culture = CultureInfo.InvariantCulture;
        var key = string.Join(";", snapshot.Catalogue.Cards.Select(c => string.Create(culture, $"{c.BookId}{c.PagesRead}{c.Status}{c.BackToday}")));
        if (key == shownKey)
        {
            return;
        }

        shownKey = key;
        foreach (var child in cards.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var card in snapshot.Catalogue.Cards)
        {
            cards.AddChild(IndexCard(card));
        }
    }

    private Button IndexCard(CatalogueCard card)
    {
        var culture = CultureInfo.InvariantCulture;
        var button = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(358, 168) };
        foreach (var state in new[] { "normal", "hover", "pressed" })
        {
            var back = state == "hover" ? UiTheme.Card.Lightened(0.3f) : state == "pressed" ? UiTheme.Card.Darkened(0.06f) : UiTheme.Card;
            button.AddThemeStyleboxOverride(state, UiTheme.Box(back, new Color("9b8467"), 1, 3, new Vector4(12, 10, 12, 10)));
        }

        button.Pressed += () => Game.OpenBook(card.BookId);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        column.OffsetLeft = 12;
        column.OffsetTop = 8;
        column.OffsetRight = -12;
        column.OffsetBottom = -8;
        column.AddThemeConstantOverride("separation", 2);
        var title = UiTheme.Para(card.Title, UiTheme.Print, 22, UiTheme.Ink);
        title.MouseFilter = MouseFilterEnum.Ignore;
        column.AddChild(title);
        var facts = UiTheme.Para(string.Create(culture, $"{card.SpineTag}  ·  {UiText.Fee} {card.Fee}  ·  {card.PagesRead} of {card.PagesTotal} {UiText.PagesRead}"), UiTheme.PrintItalic, 18, UiTheme.Faint);
        facts.MouseFilter = MouseFilterEnum.Ignore;
        column.AddChild(facts);
        var where = card.Status switch
        {
            BookStatus.OnShelf when card.BackToday => UiText.BackToday,
            BookStatus.OnShelf => UiText.OnShelf,
            BookStatus.Gone => UiText.Gone,
            _ => $"Out — {card.BorrowerName}, since {card.SinceDate}",
        };
        var status = UiTheme.Para(where, UiTheme.Print, 18, card.Status == BookStatus.OnShelf ? new Color("3d5a2e") : UiTheme.Stamp);
        status.MouseFilter = MouseFilterEnum.Ignore;
        column.AddChild(status);
        if (card.Summary.Length > 0)
        {
            var summary = UiTheme.Para($"“{card.Summary}”", UiTheme.PrintItalic, 18, UiTheme.Faint);
            summary.MouseFilter = MouseFilterEnum.Ignore;
            summary.MaxLinesVisible = 2;
            summary.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            column.AddChild(summary);
        }

        button.AddChild(column);
        // A button doesn't lay out its children, so the card follows the column's height itself: at the
        // larger text sizes the lines wrap more and a fixed card would cut them off.
        column.MinimumSizeChanged += () => button.CustomMinimumSize = new Vector2(358, Math.Max(168, column.GetCombinedMinimumSize().Y + 16));
        return button;
    }
}
