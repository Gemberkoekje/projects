namespace Curator.Core.Content;

/// <summary>The keys of a visit's <c>outcomes</c> object (CONTENT_GUIDE §6).</summary>
public static class OutcomeKeys
{
    public const string Good = "good";
    public const string Unhelpful = "unhelpful";
    public const string Harm = "harm";
    public const string HarmMisuse = "harmMisuse";
    public const string HarmAccident = "harmAccident";
    public const string Mixed = "mixed";
    public const string MixedMisuse = "mixedMisuse";
    public const string MixedAccident = "mixedAccident";
    public const string Declined = "declined";
    public const string WalkedOut = "walkedOut";

    /// <summary>Every key a visit's outcomes may use.</summary>
    public static readonly IReadOnlyList<string> All =
        [Good, Unhelpful, Harm, HarmMisuse, HarmAccident, Mixed, MixedMisuse, MixedAccident, Declined, WalkedOut];

    /// <summary>The key for a category alone: good, unhelpful, harm, mixed, declined or walkedOut.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The key.</returns>
    public static string ForCategory(OutcomeCategory category) => category switch
    {
        OutcomeCategory.Good => Good,
        OutcomeCategory.Unhelpful => Unhelpful,
        OutcomeCategory.Harm => Harm,
        OutcomeCategory.Mixed => Mixed,
        OutcomeCategory.Declined => Declined,
        OutcomeCategory.WalkedOut => WalkedOut,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "No outcome key for this category."),
    };

    /// <summary>The cause-specific key (harmMisuse, mixedAccident…), or the category key when there is no cause.</summary>
    /// <param name="category">The category.</param>
    /// <param name="cause">The cause, or <see cref="Cause.None"/>.</param>
    /// <returns>The key.</returns>
    public static string ForCategoryAndCause(OutcomeCategory category, Cause cause) =>
        cause == Cause.None ? ForCategory(category) : ForCategory(category) + cause.ToString();
}
