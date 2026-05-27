using TheCuratool.Domain;

namespace TheCuratool.Application;

/// <summary>
/// The result of a setup distribution calculation, providing the base distribution
/// and all valid target distributions after applying active setup rules.
/// </summary>
/// <param name="BaseDistribution">The unmodified player-count distribution before any rules are applied.</param>
/// <param name="ValidTargetCounts">All valid <see cref="SetupCounts"/> distributions produced by applying the active rules.</param>
public sealed record SetupCalculationResult(
    SetupCounts BaseDistribution,
    IReadOnlyList<SetupCounts> ValidTargetCounts);
