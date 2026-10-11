using Curator.Core.Projections;
using Curator.Core.Text;

namespace Curator.Presentation.Ui;

/// <summary>
/// Page markup as BBCode for a <see cref="UiTheme.Rich"/> label (BUILD_BRIEF §7.8): translated text in
/// ink, untranslated words in grey script, illegible passages as faded scribble.
/// </summary>
public static class PageText
{
    /// <summary>Spans as BBCode.</summary>
    /// <param name="spans">The parsed spans.</param>
    /// <returns>The BBCode.</returns>
    public static string Bbcode(IReadOnlyList<TextSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        return string.Concat(spans.Select(span => span.Kind switch
        {
            SpanKind.Untranslated => Untranslated(span.Text),
            SpanKind.Illegible => $"[code][color=#{UiTheme.Illegible.ToHtml(false)}]{Escape(span.Text)}[/color][/code]",
            _ => Escape(span.Text),
        }));
    }

    /// <summary>A page's ingredients: known ones in ink, unknown ones in grey script.</summary>
    /// <param name="ingredients">The ingredients.</param>
    /// <returns>The BBCode.</returns>
    public static string Ingredients(IReadOnlyList<IngredientView> ingredients)
    {
        ArgumentNullException.ThrowIfNull(ingredients);
        return string.Join(", ", ingredients.Select(i => i.Known ? Escape(i.Name) : Untranslated(i.Name)));
    }

    /// <summary>Text made safe for BBCode.</summary>
    /// <param name="text">Plain text.</param>
    /// <returns>The text with brackets escaped.</returns>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("[", "[lb]", StringComparison.Ordinal);
    }

    private static string Untranslated(string text) =>
        $"[i][color=#{UiTheme.Untranslated.ToHtml(false)}]{Escape(text)}[/color][/i]";
}
