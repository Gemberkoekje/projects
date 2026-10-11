namespace Curator.Core.Content;

/// <summary>
/// game/content/outcomes_generic.json: fallback outcomes by category and cause, used when a
/// visit defines none. Texts may use {name} and {book}.
/// </summary>
public sealed record OutcomesGenericFile
{
    public required IReadOnlyList<Outcome> Good { get; init; }

    public required IReadOnlyList<Outcome> Unhelpful { get; init; }

    public required IReadOnlyList<Outcome> HarmMisuse { get; init; }

    public required IReadOnlyList<Outcome> HarmAccident { get; init; }

    public required IReadOnlyList<Outcome> MixedMisuse { get; init; }

    public required IReadOnlyList<Outcome> MixedAccident { get; init; }

    public required bool Placeholder { get; init; }

    /// <summary>The outcomes for a category and cause, keyed the same way visits key theirs.</summary>
    /// <param name="key">good, unhelpful, harmMisuse, harmAccident, mixedMisuse or mixedAccident.</param>
    /// <returns>The outcomes, or an empty list for any other key.</returns>
    public IReadOnlyList<Outcome> For(string key) => key switch
    {
        OutcomeKeys.Good => Good,
        OutcomeKeys.Unhelpful => Unhelpful,
        OutcomeKeys.HarmMisuse => HarmMisuse,
        OutcomeKeys.HarmAccident => HarmAccident,
        OutcomeKeys.MixedMisuse => MixedMisuse,
        OutcomeKeys.MixedAccident => MixedAccident,
        _ => [],
    };
}
