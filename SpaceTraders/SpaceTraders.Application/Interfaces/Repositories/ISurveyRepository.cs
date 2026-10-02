using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

/// <summary>
/// The surveys our ships took, until they expire or the API refuses them (exhausted), with how often
/// each was extracted with (PLAN.md slice 6.4).
/// </summary>
public interface ISurveyRepository
{
    /// <summary>Stores the surveys a ship has just taken.</summary>
    /// <param name="shipSymbol">The ship that surveyed.</param>
    /// <param name="surveys">The surveys the API returned.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>A task that completes when they are stored.</returns>
    Task UpsertAsync(string shipSymbol, IReadOnlyList<SurveyModel> surveys, CancellationToken cancellationToken = default);

    /// <summary>Every stored survey that hasn't expired yet, with who took it, when, and how often it was used.</summary>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The surveys, the newest first.</returns>
    Task<IReadOnlyList<StoredSurvey>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Counts one extraction with a survey.</summary>
    /// <param name="signature">The survey's signature.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>A task that completes when it is counted.</returns>
    Task RecordExtractionAsync(string signature, CancellationToken cancellationToken = default);

    /// <summary>Removes a survey the API refused: exhausted, expired or not valid.</summary>
    /// <param name="signature">The survey's signature.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>The survey as it was stored; empty when it wasn't (any more).</returns>
    Task<IReadOnlyList<StoredSurvey>> RemoveAsync(string signature, CancellationToken cancellationToken = default);

    /// <summary>Removes the surveys that have expired by <paramref name="now"/>.</summary>
    /// <param name="now">The time to judge by.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>The surveys removed, as they were stored.</returns>
    Task<IReadOnlyList<StoredSurvey>> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>A survey as the cache keeps it.</summary>
public sealed record StoredSurvey
{
    /// <summary>Creates a stored survey.</summary>
    /// <param name="Survey">The survey, as the API returned it and wants it back for an extraction.</param>
    /// <param name="ShipSymbol">The ship that took it.</param>
    /// <param name="SurveyedAt">When it was taken.</param>
    /// <param name="Extractions">How many extractions were made with it.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public StoredSurvey(SurveyModel Survey, string ShipSymbol, DateTimeOffset SurveyedAt, int Extractions)
    {
        this.Survey = Survey;
        this.ShipSymbol = ShipSymbol;
        this.SurveyedAt = SurveyedAt;
        this.Extractions = Extractions;
    }

    /// <summary>The survey, as the API returned it and wants it back for an extraction.</summary>
    public required SurveyModel Survey { get; init; }

    /// <summary>The ship that took it.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>When it was taken.</summary>
    public required DateTimeOffset SurveyedAt { get; init; }

    /// <summary>How many extractions were made with it.</summary>
    public required int Extractions { get; init; }
}
