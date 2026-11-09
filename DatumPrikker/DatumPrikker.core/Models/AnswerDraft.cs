namespace DatumPrikker.core.Models;

/// <summary>What a respondent filled in; <see cref="Answer.Give"/> checks it.</summary>
public sealed record AnswerDraft
{
    /// <summary>The respondent's name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The respondent's email address, as typed.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>The choices; dates left out count as <see cref="Availability.None"/>.</summary>
    public IReadOnlyList<OptionAnswer> Choices { get; init; } = [];

    /// <summary>Optional remark for the maker.</summary>
    public string Comment { get; init; } = string.Empty;
}
