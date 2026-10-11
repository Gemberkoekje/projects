using System.Globalization;
using Curator.Presentation.Game;
using Godot;

namespace Curator.Presentation.Ui;

/// <summary>
/// The HUD (BUILD_BRIEF §7.7): mana and money top left; day, date and phase top centre; during a
/// visit, who is waiting, their patience and what they asked for, so looking down never loses them.
/// Also the pan tabs at the top and bottom edge (§7.2).
/// </summary>
public partial class Hud : Control, IGameView
{
    private readonly Pips mana = new();
    private readonly Label money = UiTheme.Text("", UiTheme.Print, 24, UiTheme.SpeechInk);
    private readonly Label date = UiTheme.Text("", UiTheme.Print, 24, UiTheme.SpeechInk);
    private readonly PanelContainer visitBox = new();
    private readonly Label patron = UiTheme.Text("", UiTheme.Print, 24, UiTheme.SpeechInk);
    private readonly Pips patience = new();
    private readonly Label request = UiTheme.Text("", UiTheme.Print, 20, UiTheme.SpeechInk);
    private readonly Label tell = UiTheme.Text("", UiTheme.PrintItalic, 20, UiTheme.ThoughtGlow);
    private readonly Button downTab = new();
    private readonly Button upTab = new();
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the HUD first.");

    /// <summary>Connects the HUD to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    public void Bind(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(1920, 1080);
        var left = Box();
        left.Position = new Vector2(18, 14);
        var leftRow = new HBoxContainer();
        leftRow.AddThemeConstantOverride("separation", 12);
        leftRow.AddChild(UiTheme.Text(UiText.Mana, UiTheme.PrintItalic, 20, UiTheme.CuratorInk));
        mana.Color = new Color("c9d8ff");
        mana.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        leftRow.AddChild(mana);
        leftRow.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0) });
        leftRow.AddChild(UiTheme.Text(UiText.Money, UiTheme.PrintItalic, 20, UiTheme.CuratorInk));
        leftRow.AddChild(money);
        left.AddChild(leftRow);
        AddChild(left);

        var top = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(0, 14), Size = new Vector2(1920, 48) };
        var dateBox = Box();
        date.AutowrapMode = TextServer.AutowrapMode.Off;
        dateBox.AddChild(date);
        top.AddChild(dateBox);
        AddChild(top);

        visitBox.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.SpeechBack, new Color(0.95f, 0.85f, 0.65f, 0.25f), 1, 6, new Vector4(16, 8, 16, 10)));
        visitBox.Position = new Vector2(1920 - 18 - 520, 14);
        visitBox.CustomMinimumSize = new Vector2(520, 0);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 14);
        patron.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        patron.AutowrapMode = TextServer.AutowrapMode.Off;
        nameRow.AddChild(patron);
        nameRow.AddChild(UiTheme.Text(UiText.Patience, UiTheme.PrintItalic, 18, UiTheme.CuratorInk));
        patience.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        nameRow.AddChild(patience);
        column.AddChild(nameRow);
        column.AddChild(request);
        column.AddChild(tell);
        visitBox.AddChild(column);
        AddChild(visitBox);

        Tab(downTab, UiText.Desk, () => Game.World.Camera.LookDown());
        downTab.Position = new Vector2(830, 1080 - 46);
        Tab(upTab, UiText.Counter, () => Game.World.Camera.LookUp());
        upTab.Position = new Vector2(830, 70);
        AddChild(downTab);
        AddChild(upTab);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Closing has already started tomorrow (Q8): behind a summary the HUD would show the wrong day.
        Visible = !snapshot.SummaryShowing;
        var hud = snapshot.Hud;
        var culture = CultureInfo.InvariantCulture;
        mana.Show(hud.Mana, Math.Max(hud.Mana, 1));
        money.Text = hud.Money.ToString(culture);
        money.AddThemeColorOverride("font_color", hud.Money < 0 ? new Color("ff9a8a") : UiTheme.SpeechInk);
        date.Text = hud.Day > 0
            ? string.Create(culture, $"Day {hud.Day} of {hud.WeekDays}  ·  {hud.Date}  ·  {UiText.Phase(hud.Phase)}")
            : "";
        // Looking up, the speech panel shows all of this; the HUD repeats it only at the desk (§7.6).
        visitBox.Visible = hud.InVisit && !snapshot.LookingUp;
        if (hud.InVisit)
        {
            patron.Text = hud.PatronName;
            patience.Show(hud.Patience, Math.Max(hud.Patience, 1));
            request.Text = hud.RequestedBookTitle.Length > 0
                ? $"Asks for {hud.RequestedBookTitle} ({hud.Topic})"
                : $"Asks for something on {hud.Topic}";
            tell.Text = hud.Impatient ? hud.ImpatientTell : "";
            tell.Visible = hud.Impatient;
        }

        downTab.Visible = snapshot.LookingUp;
        upTab.Visible = !snapshot.LookingUp;
    }

    private static PanelContainer Box()
    {
        var box = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.SpeechBack, new Color(0.95f, 0.85f, 0.65f, 0.25f), 1, 6, new Vector4(16, 6, 16, 6)));
        return box;
    }

    private static void Tab(Button tab, string text, Action pressed)
    {
        tab.Text = text;
        tab.FocusMode = FocusModeEnum.None;
        tab.Pressed += pressed;
        tab.CustomMinimumSize = new Vector2(260, 40);
        tab.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.SpeechBack, new Color(0.95f, 0.85f, 0.65f, 0.4f), 1, 8, new Vector4(12, 4, 12, 4)));
        tab.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.2f, 0.14f, 0.1f, 0.9f), new Color(0.95f, 0.85f, 0.65f, 0.7f), 1, 8, new Vector4(12, 4, 12, 4)));
        tab.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.2f, 0.14f, 0.1f, 0.9f), new Color(0.95f, 0.85f, 0.65f, 0.7f), 1, 8, new Vector4(12, 4, 12, 4)));
        tab.AddThemeColorOverride("font_color", UiTheme.SpeechInk);
        tab.AddThemeColorOverride("font_hover_color", Colors.White);
        tab.AddThemeColorOverride("font_pressed_color", Colors.White);
    }
}
