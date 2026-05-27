namespace TheCuratool.Domain;

/// <summary>
/// Describes a Blood on the Clocktower character as loaded from the character database or inferred from a script.
/// </summary>
/// <param name="Id">The canonical lowercase identifier for this character (e.g. <c>"imp"</c>).</param>
/// <param name="DisplayName">The human-readable name shown to players.</param>
/// <param name="Type">The <see cref="CharacterType"/> category (Townsfolk, Outsider, Minion, Demon, etc.).</param>
/// <param name="SetupRules">Rules that modify the character distribution when this character is in play.</param>
/// <param name="AvailabilityConstraints">Dynamic constraints that can block this character from being offered during a draft.</param>
/// <param name="IsUnknown">When <see langword="true"/>, this character was not found in the database and was inferred from the script.</param>
/// <param name="IsDraftExcluded">When <see langword="true"/>, this character is excluded from drafting and curated offers.</param>
/// <param name="IsOutOfScript">When <see langword="true"/>, this character was injected into the effective script for the current session.</param>
public sealed record CharacterDefinition(
    string Id,
    string DisplayName,
    CharacterType Type,
    IReadOnlyList<ISetupRule> SetupRules,
    IReadOnlyList<IAvailabilityConstraint> AvailabilityConstraints,
    bool IsUnknown,
    bool IsDraftExcluded = false,
    bool IsOutOfScript = false);
