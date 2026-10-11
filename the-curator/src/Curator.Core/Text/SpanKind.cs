namespace Curator.Core.Text;

/// <summary>How much of a span of page text the curator can read (CONTENT_GUIDE §2).</summary>
public enum SpanKind
{
    None = 0,

    /// <summary>Ordinary ink: the curator knows what it says.</summary>
    Translated,

    /// <summary>Grey cursive: the letters are readable, the meaning isn't. Written <c>{?…?}</c>.</summary>
    Untranslated,

    /// <summary>Faded scribble: unreadable. Written <c>{~…~}</c>.</summary>
    Illegible,
}
