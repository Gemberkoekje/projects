using System.Globalization;
using Curator.Core.Projections;
using Curator.Presentation.Game;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Desk;

/// <summary>
/// The curator's notebook (BUILD_BRIEF §7.5, GDD §9): a page per patron, opening on the one at the
/// counter. The header writes itself; watched loans have a note column; free notes are the curator's
/// own, in the handwriting font, and the game never reads them.
/// </summary>
public partial class NotebookPanel : Control, IGameView
{
    private readonly VBoxContainer tabs = new();
    private readonly VBoxContainer page = new();
    private string shownKey = "";
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the notebook first.");

    /// <summary>Connects the view to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    public void Bind(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override void _Ready()
    {
        Size = new Vector2(1166, 696);
        var book = new HBoxContainer { Size = Size };
        book.AddThemeConstantOverride("separation", 0);
        AddChild(book);

        var cover = new PanelContainer { CustomMinimumSize = new Vector2(250, 696) };
        cover.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color("4a2f22"), new Color("2a1810"), 2, 4, new Vector4(14, 18, 10, 18)));
        var coverColumn = new VBoxContainer();
        coverColumn.AddThemeConstantOverride("separation", 8);
        coverColumn.AddChild(UiTheme.Text(UiText.Notebook, UiTheme.Hand, 34, UiTheme.SpeechInk));
        var tabScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        tabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tabs.AddThemeConstantOverride("separation", 4);
        tabScroll.AddChild(tabs);
        coverColumn.AddChild(tabScroll);
        coverColumn.AddChild(UiTheme.Button(UiText.Close, () => Game.Open(DeskDocument.None)));
        cover.AddChild(coverColumn);
        book.AddChild(cover);

        var sheet = new PanelContainer { CustomMinimumSize = new Vector2(916, 696) };
        sheet.AddThemeStyleboxOverride("panel", UiTheme.PaperSheet(UiTheme.PaperNotebook, new Vector4(34, 22, 30, 22)));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(page);
        sheet.AddChild(scroll);
        book.AddChild(sheet);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var notebook = snapshot.Notebook;
        var culture = CultureInfo.InvariantCulture;
        var log = snapshot.IngredientLog;
        var key = snapshot.NotebookShowsIngredients
            ? string.Create(culture, $"ingredients|{log.Count}|{log.Sum(e => e.Seen.Count)}|{snapshot.NotebookIndex.Count}|{UiTheme.TextScale}")
            : notebook is null
            ? string.Create(culture, $"none|{snapshot.NotebookIndex.Count}|{UiTheme.TextScale}")
            : string.Create(culture, $"{notebook.PatronId}|{notebook.VisitCount}|{notebook.Trust}|{string.Join(",", notebook.Ledger.Select(l => l.LoanId + l.ReturnedDate))}|{notebook.Thoughts.Count}|{snapshot.NotebookIndex.Count}|{UiTheme.TextScale}");
        if (key == shownKey)
        {
            return;
        }

        shownKey = key;
        foreach (var child in tabs.GetChildren())
        {
            child.QueueFree();
        }

        // The ingredient log comes first, then a page per patron.
        tabs.AddChild(Tab(UiText.Ingredients, snapshot.NotebookShowsIngredients, Game.ShowIngredientLog));
        foreach (var entry in snapshot.NotebookIndex)
        {
            var showing = !snapshot.NotebookShowsIngredients && notebook is not null && entry.PatronId == notebook.PatronId;
            tabs.AddChild(Tab(entry.Name, showing, () => Game.ShowNotebookPage(entry.PatronId)));
        }

        foreach (var child in page.GetChildren())
        {
            child.QueueFree();
        }

        if (snapshot.NotebookShowsIngredients)
        {
            RenderIngredients(log);
            return;
        }

        if (notebook is null)
        {
            page.AddChild(Hand("Nobody yet.", 26, UiTheme.Faint));
            return;
        }

        RenderPage(notebook);
    }

    private static Label Hand(string text, int size, Color color) => UiTheme.Text(text, UiTheme.Hand, size, color);

    private static Button Tab(string name, bool showing, Action pressed)
    {
        var tab = UiTheme.Button(name, pressed);
        tab.Alignment = HorizontalAlignment.Left;
        tab.AddThemeFontOverride("font", UiTheme.Hand);
        tab.AddThemeFontSizeOverride("font_size", UiTheme.Size(24));
        if (showing)
        {
            tab.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.PaperColor(UiTheme.PaperNotebook), new Color("f6d48a"), 2, 4, new Vector4(10, 2, 10, 2)));
        }

        return tab;
    }

    // Every ingredient on a page read so far, as the pages write it, with the pages it was seen on.
    private void RenderIngredients(IReadOnlyList<IngredientLogEntry> log)
    {
        var culture = CultureInfo.InvariantCulture;
        page.AddChild(Hand(UiText.Ingredients, 40, UiTheme.Ink));
        page.AddChild(HandPara(UiText.IngredientLogHint, 24, UiTheme.Faint));
        page.AddChild(Rule());
        if (log.Count == 0)
        {
            page.AddChild(Hand("—", 24, UiTheme.Faint));
            return;
        }

        foreach (var entry in log)
        {
            var name = UiTheme.Rich(24);
            name.Text = PageText.Ingredients([new IngredientView(entry.Name, entry.Known)]);
            page.AddChild(name);
            var seen = UiTheme.Rich(19);
            seen.Text = string.Join("\n", entry.Seen.Select(s =>
                string.Create(culture, $"    [color=#{UiTheme.Faint.ToHtml(false)}]{PageText.Escape(s.BookTitle)}, page {s.PageNumber}:[/color] {PageText.Bbcode(s.SpellName)}")));
            page.AddChild(seen);
        }
    }

    private static Label HandPara(string text, int size, Color color) => UiTheme.Para(text, UiTheme.Hand, size, color);

    private void RenderPage(NotebookView notebook)
    {
        var culture = CultureInfo.InvariantCulture;
        page.AddChild(Hand(notebook.Name, 40, UiTheme.Ink));
        var trust = notebook.Trust >= 0 && notebook.Trust < UiText.TrustWords.Length ? UiText.TrustWords[notebook.Trust] : "";
        page.AddChild(HandPara(string.Create(culture, $"{UiText.FirstVisit}: {notebook.FirstVisitDate}    {UiText.Visits}: {notebook.VisitCount}    {UiText.Trust}: {trust}"), 25, UiTheme.Ink));
        page.AddChild(Rule());

        page.AddChild(Hand(UiText.WatchedLoans, 28, UiTheme.Faint));
        if (notebook.Ledger.Count == 0)
        {
            page.AddChild(Hand("—", 24, UiTheme.Faint));
        }
        else
        {
            var grid = new GridContainer { Columns = 4 };
            grid.AddThemeConstantOverride("h_separation", 16);
            foreach (var header in new[] { UiText.Book, UiText.Borrowed, UiText.Returned, "" })
            {
                grid.AddChild(Hand(header, 22, UiTheme.Faint));
            }

            foreach (var row in notebook.Ledger)
            {
                var title = HandPara(row.BookTitle, 24, UiTheme.Ink);
                title.CustomMinimumSize = new Vector2(240, 0);
                grid.AddChild(title);
                grid.AddChild(Hand(row.BorrowedDate, 24, UiTheme.Ink));
                grid.AddChild(Hand(row.ReturnedDate.Length > 0 ? row.ReturnedDate : "—", 24, UiTheme.Ink));
                var note = new LineEdit { Text = row.Note, PlaceholderText = UiText.LedgerNoteHint, CustomMinimumSize = new Vector2(300, 0) };
                note.AddThemeFontOverride("font", UiTheme.Hand);
                note.AddThemeFontSizeOverride("font_size", UiTheme.Size(24));
                var loanId = row.LoanId;
                note.TextChanged += text => Game.SetLedgerNote(loanId, text);
                grid.AddChild(note);
            }

            page.AddChild(grid);
        }

        page.AddChild(Rule());
        page.AddChild(Hand(UiText.Thoughts, 28, UiTheme.Faint));
        if (notebook.Thoughts.Count == 0)
        {
            page.AddChild(Hand("—", 24, UiTheme.Faint));
        }

        foreach (var thought in notebook.Thoughts)
        {
            page.AddChild(HandPara($"{thought.Date}: …{thought.Fragment}…", 25, UiTheme.Ink));
        }

        page.AddChild(Rule());
        page.AddChild(Hand(UiText.FreeNotes, 28, UiTheme.Faint));
        var free = new TextEdit
        {
            Text = notebook.FreeNotes,
            PlaceholderText = UiText.FreeNotesHint,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(820, 220),
        };
        free.AddThemeFontOverride("font", UiTheme.Hand);
        free.AddThemeFontSizeOverride("font_size", UiTheme.Size(26));
        var patronId = notebook.PatronId;
        free.TextChanged += () => Game.SetFreeNotes(patronId, free.Text);
        page.AddChild(free);
    }

    private static HSeparator Rule()
    {
        var rule = new HSeparator();
        rule.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(0.43f, 0.35f, 0.26f, 0.35f), Thickness = 1 });
        return rule;
    }
}
