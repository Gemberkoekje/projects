using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Mining;

/// <summary>
/// Which survey a miner extracts with (PLAN.md slice 6.4). An extraction with a survey yields one of the
/// survey's deposits at random, and a deposit listed twice comes up twice as often, so the best survey
/// for an ore is the one where the ore makes up the largest share of the deposits; then the larger
/// deposit, which lasts more extractions before it is exhausted; then the one that expires last.
/// </summary>
public static class SurveySelection
{
    /// <summary>
    /// Whether a survey can still be extracted with: complete, and not expired. An exhausted survey is
    /// known only when an extraction is refused, and is then removed from the cache.
    /// </summary>
    /// <param name="survey">The survey.</param>
    /// <param name="now">The time to judge by.</param>
    /// <returns>True for a survey the API will accept, as far as the cache knows.</returns>
    public static bool IsUsable(SurveyModel survey, DateTimeOffset now)
        => survey is not null
            && !string.IsNullOrWhiteSpace(survey.Signature)
            && !string.IsNullOrWhiteSpace(survey.WaypointSymbol)
            && !string.IsNullOrWhiteSpace(survey.Size)
            && survey.Deposits.Count > 0
            && survey.Expiration > now;

    /// <summary>The share of a survey's deposits that are an ore: 0 when the ore isn't among them.</summary>
    /// <param name="survey">The survey.</param>
    /// <param name="ore">The ore.</param>
    /// <returns>A share from 0 to 1.</returns>
    public static double Share(SurveyModel survey, string ore)
    {
        ArgumentNullException.ThrowIfNull(survey);

        return survey.Deposits.Count == 0
            ? 0
            : survey.Deposits.Count(deposit => deposit.Symbol.Equals(ore, StringComparison.OrdinalIgnoreCase)) / (double)survey.Deposits.Count;
    }

    /// <summary>Picks the best usable survey for an ore at a waypoint.</summary>
    /// <param name="surveys">The cached surveys.</param>
    /// <param name="waypointSymbol">Where the ship extracts.</param>
    /// <param name="ore">The ore it extracts.</param>
    /// <param name="now">The time to judge by.</param>
    /// <param name="best">The survey to extract with.</param>
    /// <returns>False when no usable survey of the waypoint holds the ore: the ship extracts without one.</returns>
    public static bool TryPickBest(
        IEnumerable<SurveyModel> surveys,
        string waypointSymbol,
        string ore,
        DateTimeOffset now,
        out SurveyModel best)
    {
        ArgumentNullException.ThrowIfNull(surveys);

        var found = surveys
            .Where(survey => IsUsable(survey, now)
                && survey.WaypointSymbol.Equals(waypointSymbol, StringComparison.OrdinalIgnoreCase)
                && Share(survey, ore) > 0)
            .OrderByDescending(survey => Share(survey, ore))
            .ThenByDescending(survey => SizeRank(survey.Size))
            .ThenByDescending(survey => survey.Expiration)
            .ThenBy(survey => survey.Signature, StringComparer.Ordinal)
            .ToList();

        best = found.Count > 0 ? found[0] : new SurveyModel(string.Empty, waypointSymbol, [], DateTimeOffset.MinValue, string.Empty);
        return found.Count > 0;
    }

    /// <summary>Whether any usable survey of a waypoint holds an ore.</summary>
    /// <param name="surveys">The cached surveys.</param>
    /// <param name="waypointSymbol">The waypoint.</param>
    /// <param name="ore">The ore.</param>
    /// <param name="now">The time to judge by.</param>
    /// <returns>True when a miner could extract the ore there with a survey.</returns>
    public static bool HasUsable(IEnumerable<SurveyModel> surveys, string waypointSymbol, string ore, DateTimeOffset now)
        => TryPickBest(surveys, waypointSymbol, ore, now, out _);

    /// <summary>How many usable surveys of a waypoint hold an ore (D27).</summary>
    /// <param name="surveys">The cached surveys.</param>
    /// <param name="waypointSymbol">The waypoint.</param>
    /// <param name="ore">The ore.</param>
    /// <param name="now">The time to judge by.</param>
    /// <returns>The number of surveys a miner could extract the ore there with.</returns>
    public static int CountUsable(IEnumerable<SurveyModel> surveys, string waypointSymbol, string ore, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(surveys);

        return surveys.Count(survey => IsUsable(survey, now)
            && survey.WaypointSymbol.Equals(waypointSymbol, StringComparison.OrdinalIgnoreCase)
            && Share(survey, ore) > 0);
    }

    /// <summary>LARGE deposits last the most extractions, SMALL ones the fewest.</summary>
    private static int SizeRank(string size) => size.ToUpperInvariant() switch
    {
        "LARGE" => 3,
        "MODERATE" => 2,
        "SMALL" => 1,
        _ => 0,
    };
}
