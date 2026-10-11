using System.Globalization;
using Curator.Core.Commands;
using Curator.Core.Game;
using Curator.Core.Projections;
using Curator.Presentation.Art;
using Curator.Presentation.Game;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Desk;

/// <summary>
/// The open book (BUILD_BRIEF §7.5): its cover, the pages identified so far in partial translation,
/// how many are unread, a note for pages that won't come into focus and a torn stub for removed
/// ones; Identify (with its cost), Offer this instead, and Stamp and lend for the requested book.
/// </summary>
public partial class BookPanel : Control, IGameView
{
    private readonly PanelContainer cover = new();
    private readonly TextureRect coverArt = new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered };
    private readonly Label title = UiTheme.Para("", UiTheme.Print, 30, UiTheme.Ink);
    private readonly Label facts = UiTheme.Text("", UiTheme.PrintItalic, 19, UiTheme.Faint);
    private readonly Label summary = UiTheme.Para("", UiTheme.PrintItalic, 20, UiTheme.Ink);
    private readonly Label status = UiTheme.Text("", UiTheme.Print, 19, UiTheme.Faint);
    private readonly Label unread = UiTheme.Text("", UiTheme.PrintItalic, 20, UiTheme.Ink);
    private readonly Button identify = new();
    private readonly Button offer = new();
    private readonly Button lend = new();
    private readonly VBoxContainer pages = new();
    private readonly ScrollContainer pageScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private ArtLayout layout = ArtLayout.Empty;
    private string bookId = "";
    private string shownKey = "";
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the book first.");

    /// <summary>Connects the view to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    /// <param name="artLayout">The layout, for fallback cover colours.</param>
    public void Bind(GameController gameController, ArtLayout artLayout)
    {
        controller = gameController;
        layout = artLayout;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Size = new Vector2(1166, 696);
        var spread = new HBoxContainer();
        spread.AddThemeConstantOverride("separation", 0);
        spread.Size = Size;
        AddChild(spread);

        var left = Page(new Vector4(30, 24, 26, 24), 440);
        var leftColumn = new VBoxContainer();
        leftColumn.AddThemeConstantOverride("separation", 8);
        cover.CustomMinimumSize = new Vector2(0, 96);
        coverArt.SetAnchorsPreset(LayoutPreset.FullRect);
        cover.AddChild(coverArt);
        leftColumn.AddChild(cover);
        leftColumn.AddChild(title);
        leftColumn.AddChild(facts);
        leftColumn.AddChild(summary);
        leftColumn.AddChild(status);
        leftColumn.AddChild(unread);
        leftColumn.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        foreach (var (button, caption, action) in new (Button, string, Action)[]
        {
            (identify, "", () => Game.Send(new Identify(bookId))),
            (offer, UiText.Offer, () => Game.Send(new OfferBook(bookId))),
            (lend, UiText.Lend, () => Game.Send(new LendRequested())),
        })
        {
            button.Text = caption;
            button.FocusMode = FocusModeEnum.None;
            button.Pressed += action;
            leftColumn.AddChild(button);
        }

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 10);
        footer.AddChild(UiTheme.Button(UiText.BackToDrawer, () => Game.Open(DeskDocument.Catalogue)));
        footer.AddChild(UiTheme.Button(UiText.Close, () => Game.Open(DeskDocument.None)));
        leftColumn.AddChild(footer);
        left.AddChild(leftColumn);
        spread.AddChild(left);

        var right = Page(new Vector4(34, 24, 30, 24), 726);
        pageScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        pages.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pages.AddThemeConstantOverride("separation", 14);
        pageScroll.AddChild(pages);
        right.AddChild(pageScroll);
        spread.AddChild(right);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var book = snapshot.Book;
        if (book is null)
        {
            return;
        }

        var culture = CultureInfo.InvariantCulture;
        var visit = snapshot.Visit;
        bookId = book.BookId;
        if (ArtLibrary.TryGet(book.CoverSlot, out var art))
        {
            coverArt.Texture = art;
            cover.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        }
        else
        {
            coverArt.Texture = null;
            var tint = layout.CoverColors.TryGetValue(book.SpineTag, out var color) ? color : new Color("6b5a48");
            cover.AddThemeStyleboxOverride("panel", UiTheme.Box(tint, tint.Darkened(0.4f), 2, 3, Vector4.Zero));
        }

        title.Text = book.Title;
        facts.Text = string.Create(culture, $"{book.SpineTag}  ·  {book.Rarity.ToString().ToLowerInvariant()}  ·  {UiText.Fee} {book.Fee}");
        summary.Text = book.Summary.Length > 0 ? $"“{book.Summary}”" : "";
        summary.Visible = book.Summary.Length > 0;
        status.Text = book.Status switch
        {
            BookStatus.OnShelf => UiText.OnShelf,
            BookStatus.Gone => UiText.Gone,
            _ => "Out on loan",
        };
        unread.Text = string.Create(culture, $"{book.UnreadCount} {(book.UnreadCount == 1 ? "page" : "pages")} {UiText.Unread}");

        var inVisit = visit.Present && !visit.Decided;
        identify.Text = string.Create(culture, $"Identify  ·  {book.IdentifyCost} {UiText.Mana.ToLowerInvariant()}{(book.CostsPatience ? $"  ·  costs {UiText.Patience.ToLowerInvariant()}" : "")}");
        identify.Disabled = !book.CanIdentify;
        identify.TooltipText = book.CanIdentify ? "" : Game.WhyNot(new Identify(book.BookId));
        offer.Visible = inVisit && !book.IsRequested;
        offer.Disabled = !book.CanOffer;
        offer.TooltipText = book.CanOffer ? "" : Game.WhyNot(new OfferBook(book.BookId));
        lend.Visible = inVisit && book.IsRequested;
        lend.Disabled = !book.CanLend;
        lend.TooltipText = book.CanLend ? "" : Game.WhyNot(new LendRequested());

        var key = string.Create(culture, $"{book.BookId}|{book.Pages.Count}|{book.ResistedPageNumbers.Count}|{book.RemovedPageNumbers.Count}|{UiTheme.TextScale}");
        if (key != shownKey)
        {
            shownKey = key;
            RenderPages(book);
        }
    }

    private static PanelContainer Page(Vector4 margins, float width)
    {
        var page = new PanelContainer { CustomMinimumSize = new Vector2(width, 696) };
        page.AddThemeStyleboxOverride("panel", UiTheme.PaperSheet(UiTheme.PaperPage, margins));
        return page;
    }

    private void RenderPages(BookView book)
    {
        foreach (var child in pages.GetChildren())
        {
            child.QueueFree();
        }

        var entries = book.Pages.Select(p => (p.Number, Page: (PageView?)p, Note: ""))
            .Concat(book.ResistedPageNumbers.Select(n => (Number: n, Page: (PageView?)null, Note: book.ResistedNote)))
            .Concat(book.RemovedPageNumbers.Select(n => (Number: n, Page: (PageView?)null, Note: book.RemovedNote)))
            .OrderBy(e => e.Number)
            .ToList();
        if (entries.Count == 0)
        {
            pages.AddChild(UiTheme.Text("No pages read yet.", UiTheme.PrintItalic, 21, UiTheme.Faint));
        }

        foreach (var (number, page, note) in entries)
        {
            pages.AddChild(UiTheme.Text($"Page {number.ToString(CultureInfo.InvariantCulture)}", UiTheme.PrintItalic, 18, UiTheme.Faint));
            if (page is null)
            {
                pages.AddChild(UiTheme.Para(note, UiTheme.PrintItalic, 21, UiTheme.Faint));
                continue;
            }

            var name = UiTheme.Rich(26);
            name.Text = PageText.Bbcode(page.SpellName);
            pages.AddChild(name);
            var text = UiTheme.Rich(21);
            text.Text = PageText.Bbcode(page.Text);
            pages.AddChild(text);
            if (page.Ingredients.Count > 0)
            {
                var ingredients = UiTheme.Rich(20);
                ingredients.Text = $"[color=#{UiTheme.Faint.ToHtml(false)}]{UiText.Ingredients}:[/color] {PageText.Ingredients(page.Ingredients)}";
                pages.AddChild(ingredients);
            }

            var rule = new HSeparator();
            rule.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(0.43f, 0.35f, 0.26f, 0.3f), Thickness = 1 });
            pages.AddChild(rule);
        }
    }
}
