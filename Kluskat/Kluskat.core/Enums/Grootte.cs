namespace Kluskat.core.Enums;

/// <summary>
/// Roughly how long a klus takes. The order is load-bearing: "Geen tijd" asks for a klus that is
/// smaller than the one on screen, by comparing these values.
/// </summary>
public enum Grootte
{
    /// <summary>Not chosen; also "no cap" on a <c>Sessie</c>.</summary>
    None = 0,

    /// <summary>About five minutes.</summary>
    Klein = 1,

    /// <summary>About a quarter of an hour.</summary>
    Middel = 2,

    /// <summary>Half an hour or more.</summary>
    Groot = 3,
}
