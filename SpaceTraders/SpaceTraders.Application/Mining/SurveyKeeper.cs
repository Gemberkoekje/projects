using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Mining;

/// <summary>
/// Keeps the surveys (PLAN.md slice 6.4): stores the ones a ship takes, counts each extraction made with
/// one, and ends them when they expire or the API refuses them. Each step is counted in the metrics and
/// written to the journal, so the survey dashboard shows whether surveying keeps up with the miners:
/// surveys that expire unused mean too many, extractions without one mean too few.
/// </summary>
public interface ISurveyKeeper
{
    /// <summary>Stores the surveys a ship has just taken.</summary>
    /// <param name="shipSymbol">The ship that surveyed.</param>
    /// <param name="targetOre">The ore it surveyed for.</param>
    /// <param name="taken">The surveys the API returned.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when they are stored.</returns>
    Task TakenAsync(string shipSymbol, string targetOre, IReadOnlyList<SurveyModel> taken, CancellationToken cancellationToken);

    /// <summary>Counts an extraction made with a survey.</summary>
    /// <param name="signature">The survey's signature.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when it is counted.</returns>
    Task UsedAsync(string signature, CancellationToken cancellationToken);

    /// <summary>Ends a survey the API refused: it can't be used again.</summary>
    /// <param name="refused">The refusal.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when it is gone.</returns>
    Task RefusedAsync(SurveyRefusedException refused, CancellationToken cancellationToken);

    /// <summary>Ends the surveys that expired by <paramref name="now"/>.</summary>
    /// <param name="now">The time to judge by.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when they are gone.</returns>
    Task ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class SurveyKeeper(
    ISurveyRepository surveys,
    IAutomationMetrics metrics,
    ILogger<SurveyKeeper> logger) : ISurveyKeeper
{
    private const string ExpiredReason = "expired";

    /// <inheritdoc />
    public async Task TakenAsync(string shipSymbol, string targetOre, IReadOnlyList<SurveyModel> taken, CancellationToken cancellationToken)
    {
        await surveys.UpsertAsync(shipSymbol, taken, cancellationToken);
        foreach (var survey in taken)
        {
            metrics.SurveyTaken(survey.WaypointSymbol, survey.Size);
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} surveyed {WaypointSymbol} for {TradeSymbol}: survey {Signature}, {Size}, deposits {Deposits}, until {Expiration}.",
                JournalEvents.Surveyed,
                shipSymbol,
                survey.WaypointSymbol,
                targetOre,
                survey.Signature,
                survey.Size,
                Deposits(survey),
                survey.Expiration);
        }
    }

    /// <inheritdoc />
    public Task UsedAsync(string signature, CancellationToken cancellationToken)
        => surveys.RecordExtractionAsync(signature, cancellationToken);

    /// <inheritdoc />
    public Task RefusedAsync(SurveyRefusedException refused, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refused);
        return EndAsync(refused.Signature, refused.Reason, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var ended in await surveys.RemoveExpiredAsync(now, cancellationToken))
        {
            Ended(ended, ExpiredReason);
        }
    }

    private async Task EndAsync(string signature, string reason, CancellationToken cancellationToken)
    {
        foreach (var ended in await surveys.RemoveAsync(signature, cancellationToken))
        {
            Ended(ended, reason);
        }
    }

    private void Ended(StoredSurvey ended, string reason)
    {
        metrics.SurveyEnded(ended.Survey.WaypointSymbol, reason, ended.Extractions > 0);
        logger.LogInformation(
            "{EventKind:l}: survey {Signature} of {WaypointSymbol} ({Size}) ended ({Reason}) after {Extractions} extractions; ship {ShipSymbol} took it at {SurveyedAt}.",
            JournalEvents.SurveyEnded,
            ended.Survey.Signature,
            ended.Survey.WaypointSymbol,
            ended.Survey.Size,
            reason,
            ended.Extractions,
            ended.ShipSymbol,
            ended.SurveyedAt);
    }

    /// <summary>A survey's deposits for the journal, the ore and how often it is listed: <c>COPPER_ORE x2, IRON_ORE</c>.</summary>
    private static string Deposits(SurveyModel survey)
        => string.Join(
            ", ",
            survey.Deposits
                .GroupBy(deposit => deposit.Symbol, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Count() > 1 ? $"{group.Key} x{group.Count()}" : group.Key));
}
