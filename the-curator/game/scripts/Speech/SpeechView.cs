using Curator.Core.Commands;
using Curator.Core.Projections;
using Curator.Presentation.Game;
using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Speech;

/// <summary>
/// The up view's conversation (BUILD_BRIEF §7.6): what has been said, in a panel above the counter
/// beside the archway; the questions beneath it, standard first and unlocked ones marked new; the
/// patron's patience; and the Read Thoughts fragment in italics with a soft glow, near their head.
/// Placed in canvas coordinates on a layer that follows the camera.
/// </summary>
public partial class SpeechView : Control, IGameView
{
    private readonly PanelContainer talk = new();
    private readonly Label patronName = UiTheme.Text("", UiTheme.Print, 30, UiTheme.SpeechInk);
    private readonly Label description = UiTheme.Para("", UiTheme.PrintItalic, 19, UiTheme.CuratorInk);
    private readonly ScrollContainer transcriptScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly VBoxContainer transcript = new();
    private readonly PanelContainer ask = new();
    private readonly Pips patience = new();
    private readonly Label tell = UiTheme.Text("", UiTheme.PrintItalic, 19, UiTheme.ThoughtGlow);
    private readonly VBoxContainer questions = new();
    private readonly Button readThoughts = new();
    private readonly VBoxContainer thought = new();
    private readonly Label fragment = UiTheme.Para("", UiTheme.PrintItalic, 30, new Color("fff6e4"));
    private readonly Label noticed = UiTheme.Para("", UiTheme.PrintItalic, 22, UiTheme.ThoughtGlow);
    private readonly Button noteThought = new();
    private string shownVisit = "";
    private int shownLines = -1;
    // Unset until Bind (a justified nullable: no controller yet); views are bound before they're shown.
    private GameController? controller;

    private GameController Game => controller ?? throw new InvalidOperationException("Bind the speech view first.");

    /// <summary>Connects the view to the controller.</summary>
    /// <param name="gameController">The controller.</param>
    public void Bind(GameController gameController) => controller = gameController;

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(1920, 720);
        var style = UiTheme.Box(UiTheme.SpeechBack, new Color(0.95f, 0.85f, 0.65f, 0.3f), 1, 8, new Vector4(22, 16, 22, 16));

        talk.AddThemeStyleboxOverride("panel", style);
        talk.Position = new Vector2(40, 84);
        talk.Size = new Vector2(660, 330);
        talk.CustomMinimumSize = talk.Size;
        var talkColumn = new VBoxContainer();
        talkColumn.AddThemeConstantOverride("separation", 6);
        patronName.AutowrapMode = TextServer.AutowrapMode.Off;
        talkColumn.AddChild(patronName);
        talkColumn.AddChild(description);
        talkColumn.AddChild(Rule());
        transcriptScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        transcript.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        transcript.AddThemeConstantOverride("separation", 10);
        transcriptScroll.AddChild(transcript);
        talkColumn.AddChild(transcriptScroll);
        talk.AddChild(talkColumn);
        AddChild(talk);

        ask.AddThemeStyleboxOverride("panel", style);
        ask.Position = new Vector2(40, 426);
        ask.Size = new Vector2(660, 286);
        ask.CustomMinimumSize = ask.Size;
        var askColumn = new VBoxContainer();
        askColumn.AddThemeConstantOverride("separation", 6);
        var patienceRow = new HBoxContainer();
        patienceRow.AddThemeConstantOverride("separation", 12);
        patienceRow.AddChild(UiTheme.Text(UiText.Patience, UiTheme.PrintItalic, 19, UiTheme.CuratorInk));
        patience.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        patienceRow.AddChild(patience);
        tell.AutowrapMode = TextServer.AutowrapMode.Off;
        tell.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        patienceRow.AddChild(tell);
        readThoughts.FocusMode = FocusModeEnum.None;
        readThoughts.Pressed += () => Game.Send(new ReadThoughts());
        patienceRow.AddChild(readThoughts);
        askColumn.AddChild(patienceRow);
        var questionScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        questions.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        questions.AddThemeConstantOverride("separation", 2);
        questionScroll.AddChild(questions);
        askColumn.AddChild(questionScroll);
        ask.AddChild(askColumn);
        AddChild(ask);

        thought.Position = new Vector2(1150, 150);
        thought.Size = new Vector2(640, 220);
        thought.AddThemeConstantOverride("separation", 10);
        fragment.CustomMinimumSize = new Vector2(640, 0);
        fragment.HorizontalAlignment = HorizontalAlignment.Center;
        fragment.AddThemeColorOverride("font_shadow_color", new Color(UiTheme.ThoughtGlow, 0.35f));
        fragment.AddThemeConstantOverride("shadow_outline_size", 10);
        fragment.AddThemeConstantOverride("shadow_offset_x", 0);
        fragment.AddThemeConstantOverride("shadow_offset_y", 0);
        thought.AddChild(fragment);
        noticed.CustomMinimumSize = new Vector2(640, 0);
        noticed.HorizontalAlignment = HorizontalAlignment.Center;
        thought.AddChild(noticed);
        var noteRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        noteThought.Text = UiText.NoteIt;
        noteThought.FocusMode = FocusModeEnum.None;
        noteThought.Pressed += () => Game.Send(new NoteThought());
        noteRow.AddChild(noteThought);
        thought.AddChild(noteRow);
        AddChild(thought);
    }

    /// <inheritdoc />
    public void Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var visit = snapshot.Visit;
        talk.Visible = visit.Present;
        ask.Visible = visit.Present && !visit.Decided;
        thought.Visible = visit.Present && visit.ReadThoughtsCast;
        if (!visit.Present)
        {
            shownVisit = "";
            shownLines = -1;
            return;
        }

        patronName.Text = visit.PatronName;
        description.Text = visit.PatronDescription;
        RenderTranscript(visit.VisitId, visit.Transcript);
        patience.Show(visit.Patience, Math.Max(visit.Patience, 1));
        tell.Text = visit.Impatient ? visit.Tell : "";

        foreach (var child in questions.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var question in visit.Questions)
        {
            var button = UiTheme.Button(question.Specific ? $"{question.Text}   ({UiText.NewQuestion})" : question.Text, () => Game.Send(new AskQuestion(question.Id)));
            button.Alignment = HorizontalAlignment.Left;
            button.Flat = true;
            button.AddThemeColorOverride("font_color", question.Specific ? new Color("ffe2a8") : UiTheme.SpeechInk);
            button.AddThemeColorOverride("font_hover_color", Colors.White);
            button.AddThemeColorOverride("font_disabled_color", new Color(UiTheme.CuratorInk, 0.55f));
            button.Disabled = question.Asked || !visit.CanAsk;
            questions.AddChild(button);
        }

        readThoughts.Text = $"{UiText.ReadThoughts}  ·  {visit.ReadThoughtsCost} {UiText.Mana.ToLowerInvariant()}";
        readThoughts.Disabled = !visit.CanReadThoughts;
        readThoughts.TooltipText = visit.CanReadThoughts ? "" : Game.WhyNot(new ReadThoughts());

        fragment.Text = visit.Fragment.Length > 0 ? visit.Fragment : visit.NoThoughtsLine;
        noticed.Text = visit.Noticed ? visit.NoticedLine : "";
        noticed.Visible = visit.Noticed;
        noteThought.Visible = visit.CanNoteThought;
    }

    private static HSeparator Rule()
    {
        var rule = new HSeparator();
        rule.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(0.95f, 0.85f, 0.65f, 0.25f), Thickness = 1 });
        return rule;
    }

    private static RichTextLabel Line(SpeechLine line)
    {
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        rich.AddThemeFontOverride("normal_font", UiTheme.Print);
        rich.AddThemeFontOverride("italics_font", UiTheme.PrintItalic);
        rich.AddThemeFontSizeOverride("normal_font_size", UiTheme.Size(22));
        rich.AddThemeFontSizeOverride("italics_font_size", UiTheme.Size(22));
        var text = PageText.Escape(line.Text);
        var curator = $"#{UiTheme.CuratorInk.ToHtml(false)}";
        var speech = $"#{UiTheme.SpeechInk.ToHtml(false)}";
        rich.Text = line.Kind switch
        {
            SpeechKind.Asked => $"[i][color={curator}]— {text}[/color][/i]",
            SpeechKind.BookOut => $"[i][color={curator}]— {text}[/color][/i]",
            SpeechKind.Offered => $"[i][color={curator}]You offer {text}.[/color][/i]",
            SpeechKind.Gossip => $"[i][color={speech}]“{text}”[/color][/i]",
            SpeechKind.Narration => $"[i][color={curator}]{text}[/color][/i]",
            _ => $"[color={speech}]“{text}”[/color]",
        };
        return rich;
    }

    private void RenderTranscript(string visitId, IReadOnlyList<SpeechLine> lines)
    {
        if (visitId == shownVisit && lines.Count == shownLines)
        {
            return;
        }

        foreach (var child in transcript.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var line in lines)
        {
            transcript.AddChild(Line(line));
        }

        shownVisit = visitId;
        shownLines = lines.Count;
        Callable.From(() => transcriptScroll.ScrollVertical = (int)transcriptScroll.GetVScrollBar().MaxValue).CallDeferred();
    }
}
