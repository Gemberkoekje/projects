using Godot;

namespace Curator.Presentation.Ui;

/// <summary>
/// Fonts, ink and paper colours, and the shared theme (BUILD_BRIEF §7.8). Fonts are the bundled OFL
/// faces in res://fonts; an unimported font file is read straight from disk, like paintings.
/// </summary>
public static class UiTheme
{
    /// <summary>Ink for translated text.</summary>
    public static readonly Color Ink = new("2b2118");

    /// <summary>Grey ink for untranslated words: the letters are readable, the meaning isn't.</summary>
    public static readonly Color Untranslated = new("6b6259");

    /// <summary>Faded ink for passages the curator can't read.</summary>
    public static readonly Color Illegible = new("a2978b");

    /// <summary>Muted ink for secondary lines.</summary>
    public static readonly Color Faint = new("74665a");

    /// <summary>The paper slots of §7.3: a photographed texture in art/painted/ wins, else these colours from layout.json.</summary>
    public const string PaperCard = "paper_card";

    /// <summary>The notebook's paper slot.</summary>
    public const string PaperNotebook = "paper_notebook";

    /// <summary>The page paper slot: open books, letters, summaries and menus.</summary>
    public const string PaperPage = "paper_page";

    /// <summary>The newspaper's paper slot.</summary>
    public const string PaperNews = "newspaper";

    /// <summary>Speech panels: dark and see-through, so the scene shows behind them.</summary>
    public static readonly Color SpeechBack = new(0.08f, 0.055f, 0.04f, 0.82f);

    /// <summary>Text on speech panels and the HUD.</summary>
    public static readonly Color SpeechInk = new("f3e6cc");

    /// <summary>The curator's own lines on speech panels.</summary>
    public static readonly Color CuratorInk = new("c3ae8e");

    /// <summary>The Read Thoughts glow.</summary>
    public static readonly Color ThoughtGlow = new(1f, 0.86f, 0.55f, 0.75f);

    /// <summary>Accent for buttons that decide a visit.</summary>
    public static readonly Color Stamp = new("7a2e22");

    private static readonly Dictionary<string, Font> Fonts = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Color> PaperFallbacks = new(StringComparer.Ordinal)
    {
        [PaperCard] = new("f4ecd9"),
        [PaperNotebook] = new("e8dcc0"),
        [PaperPage] = new("efe4cf"),
        [PaperNews] = new("e7e2d6"),
    };

    private static IReadOnlyDictionary<string, Color> paperColors = PaperFallbacks;

    /// <summary>Parchment for pages, summaries and menus.</summary>
    public static Color Paper => PaperColor(PaperPage);

    /// <summary>The library card's stock.</summary>
    public static Color Card => PaperColor(PaperCard);

    /// <summary>Newsprint.</summary>
    public static Color Newsprint => PaperColor(PaperNews);

    /// <summary>Scales all document text (the text-size setting).</summary>
    public static float TextScale { get; set; } = 1f;

    /// <summary>The print serif: IM Fell English.</summary>
    public static Font Print => Load("res://fonts/im-fell-english/IMFeENrm28P.ttf");

    /// <summary>IM Fell English italic.</summary>
    public static Font PrintItalic => Load("res://fonts/im-fell-english/IMFeENit28P.ttf");

    /// <summary>The curator's handwriting: Caveat.</summary>
    public static Font Hand => Load("res://fonts/caveat/Caveat-VariableFont_wght.ttf");

    /// <summary>Untranslated words: Pinyon Script.</summary>
    public static Font Script => Load("res://fonts/pinyon-script/PinyonScript-Regular.ttf");

    /// <summary>Illegible passages: Redacted Script.</summary>
    public static Font Scribble => Load("res://fonts/redacted-script/RedactedScript-Regular.ttf");

    /// <summary>Takes the paper colours from layout.json (§7.3: tune them there, not in code).</summary>
    /// <param name="colors">The <c>paperFallback</c> colours by slot.</param>
    public static void UsePaperColors(IReadOnlyDictionary<string, Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        paperColors = colors;
    }

    /// <summary>A paper slot's fallback colour.</summary>
    /// <param name="slot">The slot, such as <see cref="PaperCard"/>.</param>
    /// <returns>The colour.</returns>
    public static Color PaperColor(string slot) =>
        paperColors.TryGetValue(slot, out var color) ? color : PaperFallbacks.GetValueOrDefault(slot, new Color("efe4cf"));

    /// <summary>A sheet of a paper slot: its photographed texture if there is one, else its colour, with a soft shadow.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="margins">Content margins: left, top, right, bottom.</param>
    /// <returns>The style box.</returns>
    public static StyleBox PaperSheet(string slot, Vector4 margins)
    {
        if (Art.ArtLibrary.TryGet(slot, out var texture))
        {
            return new StyleBoxTexture
            {
                Texture = texture,
                ContentMarginLeft = margins.X,
                ContentMarginTop = margins.Y,
                ContentMarginRight = margins.Z,
                ContentMarginBottom = margins.W,
            };
        }

        var paper = PaperColor(slot);
        var box = Box(paper, paper.Darkened(0.25f), 1, 3, margins);
        box.ShadowColor = new Color(0f, 0f, 0f, 0.35f);
        box.ShadowSize = 6;
        box.ShadowOffset = new Vector2(3, 4);
        return box;
    }

    /// <summary>A sheet of a paper slot with the same margin on every side.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="margin">The margin.</param>
    /// <returns>The style box.</returns>
    public static StyleBox PaperSheet(string slot, float margin) => PaperSheet(slot, new Vector4(margin, margin, margin, margin));

    /// <summary>
    /// Stops scroll areas handing the wheel on when they reach an end, so scrolling a document to its
    /// top doesn't pan the camera (the wheel still pans over the room itself).
    /// </summary>
    /// <param name="root">Where to start looking.</param>
    public static void KeepScrollWheel(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);
        foreach (var control in root.FindChildren("*", "Control", true, false).OfType<Control>().Prepend(root as Control).OfType<Control>())
        {
            if (control is ScrollContainer or TextEdit)
            {
                control.MouseForcePassScrollEvents = false;
            }
        }
    }

    /// <summary>A document font size, scaled by the text-size setting.</summary>
    /// <param name="pixels">The size at 1080p and normal text size.</param>
    /// <returns>The size to use.</returns>
    public static int Size(int pixels) => Mathf.RoundToInt(pixels * TextScale);

    /// <summary>The theme every control uses: print serif in ink, paper-and-ink buttons.</summary>
    /// <returns>The theme.</returns>
    public static Theme Build()
    {
        var theme = new Theme { DefaultFont = Print };
        theme.SetColor("font_color", "Label", Ink);
        foreach (var (state, back, border) in new[]
        {
            ("normal", new Color("e9dcc2"), new Color("6d5843")),
            ("hover", new Color("f3e8d2"), new Color("3b2c1f")),
            ("pressed", new Color("d8c7a6"), new Color("3b2c1f")),
            ("disabled", new Color(0.86f, 0.82f, 0.74f, 0.55f), new Color(0.43f, 0.35f, 0.26f, 0.35f)),
        })
        {
            theme.SetStylebox(state, "Button", Box(back, border, 1, 6, new Vector4(14, 6, 14, 6)));
        }

        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetColor("font_color", "Button", Ink);
        theme.SetColor("font_hover_color", "Button", Ink);
        theme.SetColor("font_pressed_color", "Button", Ink);
        theme.SetColor("font_focus_color", "Button", Ink);
        theme.SetColor("font_disabled_color", "Button", new Color(0.17f, 0.13f, 0.09f, 0.45f));
        theme.SetStylebox("normal", "LineEdit", Box(new Color(1f, 1f, 1f, 0.25f), new Color(0.43f, 0.35f, 0.26f, 0.5f), 1, 3, new Vector4(6, 2, 6, 2)));
        theme.SetStylebox("focus", "LineEdit", Box(new Color(1f, 1f, 1f, 0.4f), new Color("3b2c1f"), 1, 3, new Vector4(6, 2, 6, 2)));
        theme.SetColor("font_color", "LineEdit", Ink);
        theme.SetColor("caret_color", "LineEdit", Ink);
        theme.SetColor("font_placeholder_color", "LineEdit", Faint);
        theme.SetStylebox("normal", "TextEdit", Box(new Color(1f, 1f, 1f, 0.18f), new Color(0.43f, 0.35f, 0.26f, 0.35f), 1, 3, new Vector4(8, 6, 8, 6)));
        theme.SetStylebox("focus", "TextEdit", Box(new Color(1f, 1f, 1f, 0.3f), new Color("3b2c1f"), 1, 3, new Vector4(8, 6, 8, 6)));
        theme.SetColor("font_color", "TextEdit", Ink);
        theme.SetColor("caret_color", "TextEdit", Ink);
        theme.SetColor("font_placeholder_color", "TextEdit", Faint);
        theme.SetColor("default_color", "RichTextLabel", Ink);
        theme.SetStylebox("panel", "TooltipPanel", Box(Paper, new Color("6d5843"), 1, 4, new Vector4(10, 6, 10, 6)));
        theme.SetColor("font_color", "TooltipLabel", Ink);
        ScaleFonts(theme);
        return theme;
    }

    /// <summary>Sizes the theme's default text and button captions for the text-size setting; controls using the theme follow at once.</summary>
    /// <param name="theme">The theme <see cref="Build"/> made.</param>
    public static void ScaleFonts(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        theme.DefaultFontSize = Size(22);
        theme.SetFontSize("font_size", "Button", Size(20));
    }

    /// <summary>A flat box with a border and rounded corners.</summary>
    /// <param name="back">Fill.</param>
    /// <param name="border">Border colour.</param>
    /// <param name="borderWidth">Border width.</param>
    /// <param name="radius">Corner radius.</param>
    /// <param name="margins">Content margins: left, top, right, bottom.</param>
    /// <returns>The box.</returns>
    public static StyleBoxFlat Box(Color back, Color border, int borderWidth, int radius, Vector4 margins)
    {
        var box = new StyleBoxFlat { BgColor = back, BorderColor = border };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        box.ContentMarginLeft = margins.X;
        box.ContentMarginTop = margins.Y;
        box.ContentMarginRight = margins.Z;
        box.ContentMarginBottom = margins.W;
        return box;
    }

    /// <summary>A sheet of paper: a soft shadow under a parchment fill.</summary>
    /// <param name="paper">The paper colour.</param>
    /// <param name="margin">Content margin on every side.</param>
    /// <returns>The box.</returns>
    public static StyleBoxFlat Sheet(Color paper, float margin)
    {
        var box = Box(paper, paper.Darkened(0.25f), 1, 3, new Vector4(margin, margin, margin, margin));
        box.ShadowColor = new Color(0f, 0f, 0f, 0.35f);
        box.ShadowSize = 6;
        box.ShadowOffset = new Vector2(3, 4);
        return box;
    }

    /// <summary>A one-line label in a given font, size and colour.</summary>
    /// <param name="text">The text.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The size in pixels, before text scaling.</param>
    /// <param name="color">The colour.</param>
    /// <returns>The label.</returns>
    public static Label Text(string text, Font font, int size, Color color)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.Off };
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", Size(size));
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>A paragraph: wraps at word boundaries and fills the width its container gives it.</summary>
    /// <param name="text">The text.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The size in pixels, before text scaling.</param>
    /// <param name="color">The colour.</param>
    /// <returns>The label.</returns>
    public static Label Para(string text, Font font, int size, Color color)
    {
        var label = Text(text, font, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    /// <summary>A rich text label set up for page markup: print serif, script for untranslated, scribble for illegible.</summary>
    /// <param name="size">The body size in pixels, before text scaling.</param>
    /// <returns>The label, growing to fit its text.</returns>
    public static RichTextLabel Rich(int size)
    {
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        rich.AddThemeFontOverride("normal_font", Print);
        rich.AddThemeFontOverride("bold_font", Print);
        rich.AddThemeFontOverride("italics_font", Script);
        rich.AddThemeFontOverride("bold_italics_font", PrintItalic);
        rich.AddThemeFontOverride("mono_font", Scribble);
        rich.AddThemeFontSizeOverride("normal_font_size", Size(size));
        rich.AddThemeFontSizeOverride("bold_font_size", Size(size + 4));
        rich.AddThemeFontSizeOverride("italics_font_size", Size(size + 6));
        rich.AddThemeFontSizeOverride("bold_italics_font_size", Size(size));
        rich.AddThemeFontSizeOverride("mono_font_size", Size(size));
        rich.AddThemeColorOverride("default_color", Ink);
        return rich;
    }

    /// <summary>Indents a control from the left: hanging text keeps its indent on every line.</summary>
    /// <param name="control">The control.</param>
    /// <param name="pixels">The indent.</param>
    /// <returns>The indented control.</returns>
    public static MarginContainer Indent(Control control, int pixels)
    {
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", pixels);
        margin.AddChild(control);
        return margin;
    }

    /// <summary>A button that never takes keyboard focus, so Space always rings the bell.</summary>
    /// <param name="text">The caption.</param>
    /// <param name="pressed">What it does.</param>
    /// <returns>The button.</returns>
    public static Button Button(string text, Action pressed)
    {
        ArgumentNullException.ThrowIfNull(pressed);
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += pressed;
        return button;
    }

    /// <summary>Lets go of the cached fonts, so nothing is left in use when the game quits.</summary>
    public static void Release() => Fonts.Clear();

    private static Font Load(string path)
    {
        if (Fonts.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Font font;
        if (ResourceLoader.Exists(path))
        {
            font = GD.Load<Font>(path);
        }
        else
        {
            var file = new FontFile();
            file.LoadDynamicFont(ProjectSettings.GlobalizePath(path));
            font = file;
        }

        Fonts[path] = font;
        return font;
    }
}
