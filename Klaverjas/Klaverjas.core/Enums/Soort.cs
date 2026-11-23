namespace Klaverjas.core.Enums;

/// <summary>What kind of player someone is (D14, R4, R14).</summary>
public enum Soort
{
    /// <summary>Not set.</summary>
    None = 0,

    /// <summary>A member: in the year ranking and the pits ranking.</summary>
    Lid = 1,

    /// <summary>A substitute: can take an evening's top 3, but is never ranked (R4).</summary>
    Invaller = 2,
}
