using Curator.Presentation.Art;
using Curator.Presentation.Game;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Desk;

/// <summary>
/// The desk in the down view (BUILD_BRIEF §7.5): the library card on the left with its decisions,
/// the morning's newspaper, letters and returns beneath it, the notebook and catalogue drawer by the
/// candle, and one document open in the middle. Placed in canvas coordinates (y 1080–1800) on a layer
/// that follows the camera.
/// </summary>
public partial class DeskView : Control, IGameView
{
    private readonly CardPanel card = new() { Name = "Card" };
    private readonly DeskItem newspaper = new() { Name = "Newspaper" };
    private readonly DeskItem letters = new() { Name = "Letters" };
    private readonly DeskItem tray = new() { Name = "Tray" };
    private readonly DeskItem notebook = new() { Name = "NotebookItem" };
    private readonly DeskItem catalogue = new() { Name = "CatalogueItem" };
    private readonly BookPanel book = new() { Name = "Book" };
    private readonly CataloguePanel drawer = new() { Name = "Catalogue" };
    private readonly NotebookPanel notebookPage = new() { Name = "Notebook" };
    private readonly NewspaperPanel paper = new() { Name = "Paper" };
    private readonly LettersPanel letterPile = new() { Name = "LetterPile" };
    private readonly TrayPanel trayPanel = new() { Name = "TrayContents" };
    private readonly Dictionary<DeskDocument, (Control Panel, IGameView View)> documents = [];
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the desk first.");

    /// <summary>Connects the desk and everything on it to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    /// <param name="layout">The art layout, for fallback cover colours.</param>
    public void Bind(GameController gameController, ArtLayout layout)
    {
        controller = gameController;
        card.Bind(gameController);
        book.Bind(gameController, layout);
        drawer.Bind(gameController);
        notebookPage.Bind(gameController);
        paper.Bind(gameController);
        letterPile.Bind(gameController);
        trayPanel.Bind(gameController);
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(0, 1080);
        Size = new Vector2(1920, 720);
        card.Position = new Vector2(24, 18);
        AddChild(card);

        Place(newspaper, UiText.Newspaper, new Color("e4dccb"), new Rect2(24, 492, 140, 210), DeskDocument.Newspaper, UiTheme.Ink);
        Place(letters, UiText.Letters, new Color("efe4cf"), new Rect2(176, 492, 140, 210), DeskDocument.Letters, UiTheme.Ink);
        Place(tray, UiText.Tray, new Color("6b4a2e"), new Rect2(328, 492, 156, 210), DeskDocument.Tray, UiTheme.SpeechInk);
        Place(notebook, UiText.Notebook, new Color("4a2f22"), new Rect2(1688, 440, 210, 124), DeskDocument.Notebook, UiTheme.SpeechInk);
        Place(catalogue, UiText.Catalogue, new Color("5a3d26"), new Rect2(1688, 578, 210, 124), DeskDocument.Catalogue, UiTheme.SpeechInk);

        var reading = new Control { Name = "Reading", Position = new Vector2(506, 12), Size = new Vector2(1166, 696), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(reading);
        foreach (var (which, panel, view) in new (DeskDocument, Control, IGameView)[]
        {
            (DeskDocument.Book, book, book),
            (DeskDocument.Catalogue, drawer, drawer),
            (DeskDocument.Notebook, notebookPage, notebookPage),
            (DeskDocument.Newspaper, paper, paper),
            (DeskDocument.Letters, letterPile, letterPile),
            (DeskDocument.Tray, trayPanel, trayPanel),
        })
        {
            panel.Visible = false;
            reading.AddChild(panel);
            documents[which] = (panel, view);
        }
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        card.Render(snapshot);
        letters.SetBadge(snapshot.Morning.Letters.Count);
        tray.SetBadge(snapshot.Morning.Returns.Count(r => !snapshot.Shelved.Contains(r.LoanId)));
        foreach (var (item, which) in new[] { (newspaper, DeskDocument.Newspaper), (letters, DeskDocument.Letters), (tray, DeskDocument.Tray), (notebook, DeskDocument.Notebook), (catalogue, DeskDocument.Catalogue) })
        {
            item.Restyle(snapshot.Document == which || (which == DeskDocument.Catalogue && snapshot.Document == DeskDocument.Book));
        }

        foreach (var (which, (panel, view)) in documents)
        {
            panel.Visible = which == snapshot.Document;
            if (panel.Visible)
            {
                view.Render(snapshot);
            }
        }
    }

    private void Place(DeskItem item, string title, Color color, Rect2 rect, DeskDocument which, Color ink)
    {
        item.Setup(title, color, ink);
        item.Position = rect.Position;
        item.Size = rect.Size;
        item.CustomMinimumSize = rect.Size;
        item.Pressed += () => Game.Toggle(which);
        AddChild(item);
    }
}
