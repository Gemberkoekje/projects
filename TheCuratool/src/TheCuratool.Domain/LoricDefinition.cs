namespace TheCuratool.Domain;

/// <summary>
/// Defines a Loric — a named modifier that alters the setup distribution when active for a session.
/// </summary>
/// <param name="Id">The canonical lowercase identifier for this Loric.</param>
/// <param name="DisplayName">The human-readable Loric name.</param>
/// <param name="SetupRules">The setup rules contributed by this Loric when it is active.</param>
public sealed record LoricDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<ISetupRule> SetupRules);
