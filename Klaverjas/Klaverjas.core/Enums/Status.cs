namespace Klaverjas.core.Enums;

/// <summary>How someone took part in an evening, in the colours of Dad's Invulblad (D1).</summary>
public enum Status
{
    /// <summary>Not set.</summary>
    None = 0,

    /// <summary>Played all three rounds (green).</summary>
    Speelt = 1,

    /// <summary>Sat out one round (yellow). The card total already includes the 1600 for it (R11).</summary>
    Slaper = 2,

    /// <summary>Was not there (light red): the fixed absence score and no pits (R3).</summary>
    Afwezig = 3,
}
