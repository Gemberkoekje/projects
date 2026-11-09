namespace DatumPrikker.core.Enums;

/// <summary>What a respondent says to one date option.</summary>
public enum Availability
{
    /// <summary>No answer for this date.</summary>
    None = 0,

    /// <summary>Ja: the date works.</summary>
    Yes = 1,

    /// <summary>Misschien: the date might work.</summary>
    Maybe = 2,

    /// <summary>Nee: the date does not work.</summary>
    No = 3,
}
