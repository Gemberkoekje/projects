using TheCuratool.Domain;

namespace TheCuratool.Application;

/// <summary>
/// Represents a candidate ability that a dynamic-setup character (Alchemist / Boffin) may borrow.
/// </summary>
/// <param name="AbilityCharacterId">The canonical ID of the character whose ability could be borrowed.</param>
/// <param name="DisplayName">The human-readable name of the ability character.</param>
/// <param name="IsAvailable">When <see langword="true"/>, this ability can currently be assigned.</param>
/// <param name="UnavailableReason">A concise explanation of why the ability is not available. Empty when <see cref="IsAvailable"/> is <see langword="true"/>.</param>
public sealed record AbilityOption(
    string AbilityCharacterId,
    string DisplayName,
    bool IsAvailable,
    string UnavailableReason);
