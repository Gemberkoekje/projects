namespace SpaceTraders.Application.Ports;

/// <summary>
/// The API refused to take materials for a construction site (PLAN.md slice 6.6): the site doesn't need the material, it
/// has all it needs of it already, or the ship isn't at the site. Supplied again, it fails again, so the trip ends.
/// </summary>
public sealed class ConstructionRefusedException : Exception
{
    /// <summary>The API's error code for a material the site doesn't need (<c>constructionMaterialNotRequired</c>).</summary>
    public const int NotRequiredErrorCode = 4800;

    /// <summary>The API's error code for a material the site has all it needs of (<c>constructionMaterialFulfilled</c>).</summary>
    public const int FulfilledErrorCode = 4801;

    /// <summary>The API's error code for a ship that isn't at the site (<c>shipConstructionInvalidLocationError</c>).</summary>
    public const int InvalidLocationErrorCode = 4802;

    /// <summary>Creates the exception.</summary>
    /// <param name="waypointSymbol">The construction site.</param>
    /// <param name="tradeSymbol">The material refused.</param>
    /// <param name="errorCode">The API's error code.</param>
    /// <param name="innerException">The API error.</param>
    public ConstructionRefusedException(string waypointSymbol, string tradeSymbol, int errorCode, Exception innerException)
        : base($"The API refused {tradeSymbol} for construction site {waypointSymbol} ({ReasonFor(errorCode)}).", innerException)
    {
        WaypointSymbol = waypointSymbol;
        TradeSymbol = tradeSymbol;
        ErrorCode = errorCode;
    }

    /// <summary>The construction site.</summary>
    public string WaypointSymbol { get; }

    /// <summary>The material refused.</summary>
    public string TradeSymbol { get; }

    /// <summary>The API's error code.</summary>
    public int ErrorCode { get; }

    /// <summary>
    /// Why, in a word for the journal: <c>not_needed</c> when the site doesn't need the material or has all of it, or
    /// <c>wrong_location</c>.
    /// </summary>
    public string Reason => ReasonFor(ErrorCode);

    /// <summary>Whether an API error code means the site refused the material.</summary>
    /// <param name="errorCode">The API's error code.</param>
    /// <returns>True for the construction error codes.</returns>
    public static bool IsConstructionRefusal(int errorCode)
        => errorCode is NotRequiredErrorCode or FulfilledErrorCode or InvalidLocationErrorCode;

    private static string ReasonFor(int errorCode)
        => errorCode == InvalidLocationErrorCode ? "wrong_location" : "not_needed";
}
