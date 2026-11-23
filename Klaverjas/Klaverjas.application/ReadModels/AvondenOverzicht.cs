namespace Klaverjas.application.ReadModels;

/// <summary>The Avonden screen: the next evening, then every evening of the season (newest first).</summary>
public sealed record AvondenOverzicht
{
    /// <summary>The next evening to enter.</summary>
    public DateOnly VolgendeAvond { get; init; }

    /// <summary>The season's entered evenings and evenings without play, newest first.</summary>
    public IReadOnlyList<AvondRegel> Regels { get; init; } = [];
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
