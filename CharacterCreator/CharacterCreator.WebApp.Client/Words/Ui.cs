using System.Globalization;
using CharacterCreator.core.Content;

namespace CharacterCreator.WebApp.Client.Words;

/// <summary>
/// The language the screen speaks, and the few things that differ with it: which of two texts to
/// show, and how a number is written (DESIGN.md decision 11).
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
/// <param name="Language">The language.</param>
public readonly record struct Ui(Language Language)
{
    /// <summary>Dutch, the default.</summary>
    public static Ui Dutch { get; } = new (Language.Dutch);

    /// <summary>Whether the screen speaks English.</summary>
    public bool IsEnglish => Language == Language.English;

    /// <summary>The language's code, as <c>html lang</c> and the language cookie write it.</summary>
    public string Code => IsEnglish ? "en" : "nl";

    /// <summary>The text in the screen's language.</summary>
    /// <param name="nl">The Dutch text.</param>
    /// <param name="en">The English text.</param>
    /// <returns>One of them.</returns>
    public string T(string nl, string en) => IsEnglish ? en : nl;

    /// <summary>A content text in the screen's language, falling back to Dutch.</summary>
    /// <param name="text">The text.</param>
    /// <returns>It, in the screen's language.</returns>
    public string T(LocalizedText text) => text?.In(Language) ?? string.Empty;

    /// <summary>
    /// A number as the language writes it: up to two decimals, a decimal comma in Dutch. Written by
    /// hand rather than through a culture, as the browser may not carry Dutch culture data.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The text, such as <c>2,5</c>.</returns>
    public string Number(decimal value)
    {
        var text = value.ToString("0.##", CultureInfo.InvariantCulture);
        return IsEnglish ? text : text.Replace('.', ',');
    }

    /// <summary>The language for a code, such as a cookie's value; Dutch for anything else.</summary>
    /// <param name="code"><c>en</c> or <c>nl</c>.</param>
    /// <returns>The screen's language.</returns>
    public static Ui For(string code)
        => string.Equals(code?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? new (Language.English) : Dutch;
}
