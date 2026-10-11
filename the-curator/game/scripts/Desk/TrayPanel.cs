using Curator.Core.Commands;
using Curator.Presentation.Game;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Desk;

/// <summary>
/// The returns tray (BUILD_BRIEF §7.5): books that came back this morning, each with "Note it" while
/// its return can be written into the notebook, and a click to put it back on the shelf.
/// </summary>
public partial class TrayPanel : Control, IGameView
{
    private readonly VBoxContainer body = new();
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the tray first.");

    /// <summary>Connects the view to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    public void Bind(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override void _Ready()
    {
        Size = new Vector2(1166, 696);
        var tray = new PanelContainer { Size = new Vector2(900, 520), Position = new Vector2(133, 60) };
        tray.CustomMinimumSize = tray.Size;
        tray.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color("6b4a2e"), new Color("3a2614"), 4, 6, new Vector4(26, 22, 26, 22)));
        AddChild(tray);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(body);
        tray.AddChild(scroll);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var child in body.GetChildren())
        {
            child.QueueFree();
        }

        body.AddChild(UiTheme.Text(UiText.Tray, UiTheme.Print, 28, UiTheme.SpeechInk));
        var waiting = snapshot.Morning.Returns.Where(r => !snapshot.Shelved.Contains(r.LoanId)).ToList();
        if (waiting.Count == 0)
        {
            body.AddChild(UiTheme.Text(UiText.NothingReturned, UiTheme.PrintItalic, 22, UiTheme.CuratorInk));
        }

        foreach (var book in waiting)
        {
            var slip = new PanelContainer();
            slip.AddThemeStyleboxOverride("panel", UiTheme.PaperSheet(UiTheme.PaperCard, 14));
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            column.AddChild(UiTheme.Para(book.Title, UiTheme.Print, 24, UiTheme.Ink));
            column.AddChild(UiTheme.Text($"{UiText.ReturnedBy} {book.PatronName}", UiTheme.PrintItalic, 19, UiTheme.Faint));
            if (book.RemovedPageIds.Count > 0)
            {
                column.AddChild(UiTheme.Text(UiText.PagesTorn, UiTheme.PrintItalic, 19, UiTheme.Stamp));
            }

            row.AddChild(column);
            var loanId = book.LoanId;
            if (book.CanNoteReturn)
            {
                row.AddChild(UiTheme.Button(UiText.NoteIt, () => Game.Send(new NoteReturn(loanId))));
            }
            else if (book.Noted)
            {
                var noted = UiTheme.Button(UiText.Noted, () => { });
                noted.Disabled = true;
                row.AddChild(noted);
            }

            row.AddChild(UiTheme.Button(UiText.Shelve, () => Game.Shelve(loanId)));
            slip.AddChild(row);
            body.AddChild(slip);
        }

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        footer.AddChild(UiTheme.Button(UiText.Close, () => Game.Open(DeskDocument.None)));
        body.AddChild(footer);
    }
}
