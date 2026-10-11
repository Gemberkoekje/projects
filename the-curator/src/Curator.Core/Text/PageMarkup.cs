using System.Text;

namespace Curator.Core.Text;

/// <summary>
/// Parses page markup: plain text is translated, <c>{?…?}</c> is untranslated and
/// <c>{~…~}</c> is illegible. Spans don't nest and are never empty.
/// </summary>
public static class PageMarkup
{
    private const string UntranslatedOpen = "{?";
    private const string UntranslatedClose = "?}";
    private const string IllegibleOpen = "{~";
    private const string IllegibleClose = "~}";

    /// <summary>Splits markup into spans.</summary>
    /// <param name="markup">The markup.</param>
    /// <returns>The spans in order; adjacent text of one kind is one span.</returns>
    /// <exception cref="FormatException">The markup is unbalanced, nested or has an empty span.</exception>
    public static IReadOnlyList<TextSpan> Parse(string markup)
    {
        var (spans, errors) = Scan(markup);
        if (errors.Count > 0)
        {
            throw new FormatException($"Bad page markup \"{markup}\": {string.Join("; ", errors)}");
        }

        return spans;
    }

    /// <summary>Checks markup without throwing.</summary>
    /// <param name="markup">The markup.</param>
    /// <returns>A description of each problem; empty when the markup is valid.</returns>
    public static IReadOnlyList<string> Validate(string markup) => Scan(markup).Errors;

    /// <summary>The text a reader can make out, with illegible runs dropped: handy for word counts and tests.</summary>
    /// <param name="markup">Valid markup.</param>
    /// <returns>The translated and untranslated text.</returns>
    public static string Legible(string markup) =>
        string.Concat(Parse(markup).Where(s => s.Kind != SpanKind.Illegible).Select(s => s.Text));

    private static (IReadOnlyList<TextSpan> Spans, IReadOnlyList<string> Errors) Scan(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        var spans = new List<TextSpan>();
        var errors = new List<string>();
        var plain = new StringBuilder();
        var i = 0;
        while (i < markup.Length)
        {
            var kind = OpenerAt(markup, i);
            if (kind == SpanKind.None)
            {
                if (At(markup, i, UntranslatedClose) || At(markup, i, IllegibleClose))
                {
                    errors.Add($"closing marker at {i} without an opening one");
                    i += 2;
                    continue;
                }

                plain.Append(markup[i]);
                i++;
                continue;
            }

            var close = kind == SpanKind.Untranslated ? UntranslatedClose : IllegibleClose;
            var end = markup.IndexOf(close, i + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                errors.Add($"span opened at {i} is never closed");
                break;
            }

            var inner = markup[(i + 2)..end];
            if (inner.Contains(UntranslatedOpen, StringComparison.Ordinal) || inner.Contains(IllegibleOpen, StringComparison.Ordinal))
            {
                errors.Add($"span opened at {i} contains another span");
            }

            var otherClose = kind == SpanKind.Untranslated ? IllegibleClose : UntranslatedClose;
            if (inner.Contains(otherClose, StringComparison.Ordinal))
            {
                errors.Add($"span opened at {i} contains a stray '{otherClose}'");
            }

            if (inner.Trim().Length == 0)
            {
                errors.Add($"span opened at {i} is empty");
            }

            Flush(plain, spans);
            spans.Add(new TextSpan(kind, inner));
            i = end + 2;
        }

        Flush(plain, spans);
        return (spans, errors);
    }

    private static void Flush(StringBuilder plain, List<TextSpan> spans)
    {
        if (plain.Length > 0)
        {
            spans.Add(new TextSpan(SpanKind.Translated, plain.ToString()));
            plain.Clear();
        }
    }

    private static SpanKind OpenerAt(string markup, int index)
    {
        if (At(markup, index, UntranslatedOpen))
        {
            return SpanKind.Untranslated;
        }

        return At(markup, index, IllegibleOpen) ? SpanKind.Illegible : SpanKind.None;
    }

    private static bool At(string markup, int index, string marker) =>
        string.CompareOrdinal(markup, index, marker, 0, marker.Length) == 0;
}
