namespace CharacterCreator.core.Content;

/// <summary>The languages the Character Creator speaks (DESIGN.md §2, decision 11).</summary>
public enum Language
{
    /// <summary>Not chosen.</summary>
    None = 0,

    /// <summary>Dutch, the rulebook's language.</summary>
    Dutch,

    /// <summary>English.</summary>
    English,
}

/// <summary>A text in Dutch and English.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record LocalizedText
{
    /// <summary>No text in either language.</summary>
    public static LocalizedText Empty { get; } = new () { Nl = string.Empty, En = string.Empty };

    /// <summary>The Dutch text.</summary>
    required public string Nl { get; init; }

    /// <summary>The English text.</summary>
    required public string En { get; init; }

    /// <summary>The text in a language, falling back to Dutch where the English is missing.</summary>
    /// <param name="language">The language.</param>
    /// <returns>The text; empty when there is none.</returns>
    public string In(Language language)
        => language == Language.English && !string.IsNullOrWhiteSpace(En) ? En : Nl ?? string.Empty;
}
