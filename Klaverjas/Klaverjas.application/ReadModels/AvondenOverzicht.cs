namespace Klaverjas.application.ReadModels;

/// <summary>The Avonden screen: the next evening, then every evening of the season (newest first).</summary>
public sealed record AvondenOverzicht
{
    /// <summary>The next evening to enter.</summary>
    public DateOnly VolgendeAvond { get; init; }

    /// <summary>Whether the next evening has been prepared (cards and tables).</summary>
    public bool VolgendeVoorbereid { get; init; }

    /// <summary>The season's entered evenings and evenings without play, newest first.</summary>
    public IReadOnlyList<AvondRegel> Regels { get; init; } = [];

    /// <summary>The latest evening in the standings online; <see cref="DateOnly.MinValue"/> when nothing is online.</summary>
    public DateOnly OnlineTot { get; init; } = DateOnly.MinValue;

    /// <summary>When the standings went online (UTC).</summary>
    public DateTime OnlineSinds { get; init; } = DateTime.MinValue;

    /// <summary>Whether the standings changed after they were put online.</summary>
    public bool OnlineGewijzigd { get; init; }

    /// <summary>Whether an evening's results are in the standings online.</summary>
    /// <param name="datum">The evening.</param>
    /// <returns><see langword="true"/> up to the latest evening online.</returns>
    public bool IsOnline(DateOnly datum) => OnlineTot != DateOnly.MinValue && datum <= OnlineTot;
}

/// <summary>One line of the Avonden screen.</summary>
public sealed record AvondRegel
{
    /// <summary>The date.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>How many played (members, slapers and substitutes).</summary>
    public int Gespeeld { get; init; }

    /// <summary>How many sat out a round.</summary>
    public int Slapers { get; init; }

    /// <summary>Why there is no play; empty for an entered evening.</summary>
    public string GeenSpeelavond { get; init; } = string.Empty;

    /// <summary>Whether this is an evening without play.</summary>
    public bool IsGeenSpeelavond => GeenSpeelavond.Length > 0;
}
