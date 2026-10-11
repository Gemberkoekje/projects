using Curator.Core.Commands;
using Curator.Presentation.Game;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Desk;

/// <summary>
/// The waiting patron's library card (BUILD_BRIEF §7.5): name, description, first visit and every
/// loan, true and factual. During a visit "Stamp and lend" and "Hand back the card" sit beneath it;
/// after a lend, the stamp's slip offers "Note it".
/// </summary>
public partial class CardPanel : Control, IGameView
{
    private readonly PanelContainer card = new();
    private readonly Label heading = UiTheme.Text(UiText.LibraryCard, UiTheme.PrintItalic, 18, UiTheme.Faint);
    private readonly Label patronName = UiTheme.Text("", UiTheme.Print, 30, UiTheme.Ink);
    private readonly Label description = UiTheme.Para("", UiTheme.PrintItalic, 19, UiTheme.Ink);
    private readonly Label firstVisit = UiTheme.Text("", UiTheme.Print, 19, UiTheme.Faint);
    private readonly GridContainer loans = new() { Columns = 3 };
    private readonly Label empty = UiTheme.Text(UiText.NoCard, UiTheme.PrintItalic, 22, new Color(0.95f, 0.88f, 0.75f, 0.55f));
    private readonly HBoxContainer decisions = new();
    private readonly Button lend = new();
    private readonly Button decline = new();
    private readonly PanelContainer slip = new();
    private readonly Label slipText = UiTheme.Text(UiText.Stamped, UiTheme.PrintItalic, 20, UiTheme.Ink);
    private readonly Button noteLoan = new();
    private string noteLoanId = "";
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the card first.");

    /// <summary>Connects the view to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    public void Bind(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override void _Ready()
    {
        Size = new Vector2(460, 470);
        MouseFilter = MouseFilterEnum.Ignore;
        card.AddThemeStyleboxOverride("panel", UiTheme.Sheet(UiTheme.Card, 18));
        card.Size = new Vector2(460, 330);
        card.CustomMinimumSize = card.Size;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        column.AddChild(heading);
        patronName.AutowrapMode = TextServer.AutowrapMode.Off;
        column.AddChild(patronName);
        column.AddChild(description);
        column.AddChild(firstVisit);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        loans.AddThemeConstantOverride("h_separation", 18);
        loans.AddThemeConstantOverride("v_separation", 2);
        scroll.AddChild(loans);
        column.AddChild(scroll);
        card.AddChild(column);
        AddChild(card);

        empty.Position = new Vector2(20, 140);
        empty.Size = new Vector2(420, 40);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(empty);

        decisions.Position = new Vector2(0, 342);
        decisions.AddThemeConstantOverride("separation", 12);
        lend.Text = UiText.Lend;
        lend.FocusMode = FocusModeEnum.None;
        lend.Pressed += () => Game.Send(new LendRequested());
        decline.Text = UiText.Decline;
        decline.FocusMode = FocusModeEnum.None;
        decline.Pressed += () => Game.Send(new Decline());
        decisions.AddChild(lend);
        decisions.AddChild(decline);
        AddChild(decisions);

        slip.AddThemeStyleboxOverride("panel", UiTheme.Sheet(UiTheme.Paper, 10));
        slip.Position = new Vector2(0, 398);
        var slipRow = new HBoxContainer();
        slipRow.AddThemeConstantOverride("separation", 14);
        slipRow.AddChild(slipText);
        noteLoan.Text = UiText.NoteIt;
        noteLoan.FocusMode = FocusModeEnum.None;
        noteLoan.Pressed += () => Game.Send(new NoteLoan(noteLoanId));
        slipRow.AddChild(noteLoan);
        slip.AddChild(slipRow);
        AddChild(slip);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var visit = snapshot.Visit;
        var shown = snapshot.Card;
        card.Visible = shown is not null;
        empty.Visible = shown is null;
        if (shown is not null)
        {
            patronName.Text = shown.Name;
            description.Text = shown.Description;
            firstVisit.Text = $"{UiText.FirstVisit}: {shown.FirstVisitDate}";
            foreach (var child in loans.GetChildren())
            {
                child.QueueFree();
            }

            if (shown.Loans.Count == 0)
            {
                loans.AddChild(UiTheme.Text(UiText.NoLoans, UiTheme.PrintItalic, 19, UiTheme.Faint));
            }
            else
            {
                foreach (var header in new[] { UiText.Book, UiText.Borrowed, UiText.Returned })
                {
                    loans.AddChild(UiTheme.Text(header, UiTheme.PrintItalic, 18, UiTheme.Faint));
                }

                foreach (var row in shown.Loans)
                {
                    var title = UiTheme.Para(row.BookTitle, UiTheme.Print, 19, UiTheme.Ink);
                    title.CustomMinimumSize = new Vector2(190, 0);
                    loans.AddChild(title);
                    loans.AddChild(UiTheme.Text(row.BorrowedDate, UiTheme.Print, 19, UiTheme.Ink));
                    loans.AddChild(UiTheme.Text(row.ReturnedDate.Length > 0 ? row.ReturnedDate : "—", UiTheme.Print, 19, UiTheme.Ink));
                }
            }
        }

        decisions.Visible = visit.Present && !visit.Decided;
        lend.Disabled = !visit.CanLendRequested;
        lend.TooltipText = visit.CanLendRequested ? "" : Game.WhyNot(new LendRequested());
        decline.Disabled = !visit.CanDecline;
        decline.TooltipText = visit.CanDecline ? "" : Game.WhyNot(new Decline());
        noteLoanId = visit.NoteLoanId;
        slip.Visible = visit.Present && visit.Decided && visit.NoteLoanId.Length > 0;
    }
}
