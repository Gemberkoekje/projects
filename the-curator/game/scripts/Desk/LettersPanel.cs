using Curator.Core.Game;
using Curator.Presentation.Game;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Desk;

/// <summary>Today's letters (BUILD_BRIEF §7.5): the Board's in print, patrons' in their own hand.</summary>
public partial class LettersPanel : Control, IGameView
{
    private readonly VBoxContainer body = new();
    private string shownKey = "";
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the letters first.");

    /// <summary>Connects the view to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    public void Bind(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override void _Ready()
    {
        Size = new Vector2(1166, 696);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Size = new Vector2(1000, 696), Position = new Vector2(83, 0) };
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 18);
        scroll.AddChild(body);
        AddChild(scroll);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var letters = snapshot.Morning.Letters;
        var key = $"{snapshot.Hud.Day}|{letters.Count}|{UiTheme.TextScale}";
        if (key == shownKey)
        {
            return;
        }

        shownKey = key;
        foreach (var child in body.GetChildren())
        {
            child.QueueFree();
        }

        if (letters.Count == 0)
        {
            var none = new PanelContainer();
            none.AddThemeStyleboxOverride("panel", UiTheme.PaperSheet(UiTheme.PaperPage, 26));
            none.AddChild(UiTheme.Text(UiText.NoLetters, UiTheme.PrintItalic, 24, UiTheme.Faint));
            body.AddChild(none);
        }

        foreach (var letter in letters)
        {
            var sheet = new PanelContainer();
            sheet.AddThemeStyleboxOverride("panel", UiTheme.PaperSheet(UiTheme.PaperPage, 28));
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 8);
            column.AddChild(UiTheme.Text($"From {letter.From}", UiTheme.PrintItalic, 20, UiTheme.Faint));
            if (letter.Title.Length > 0)
            {
                column.AddChild(UiTheme.Para(letter.Title, UiTheme.Print, 28, UiTheme.Ink));
            }

            var handwritten = letter.Kind == LetterKind.Outcome;
            column.AddChild(UiTheme.Para(letter.Text, handwritten ? UiTheme.Hand : UiTheme.Print, handwritten ? 28 : 22, UiTheme.Ink));
            sheet.AddChild(column);
            body.AddChild(sheet);
        }

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(UiTheme.Button(UiText.Close, () => Game.Open(DeskDocument.None)));
        body.AddChild(row);
    }
}
