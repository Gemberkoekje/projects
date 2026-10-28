namespace Kluskat.core.Enums;

/// <summary>Why a klus was skipped: the two buttons under the klus card.</summary>
public enum OverslaanReden
{
    /// <summary>Not given.</summary>
    None = 0,

    /// <summary>"Geen zin": any other klus will do.</summary>
    GeenZin = 1,

    /// <summary>"Geen tijd": the next klus has to be smaller than this one.</summary>
    GeenTijd = 2,
}
